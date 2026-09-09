#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Lightning;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.LightningAddressConnect;

/// <summary>
/// Shared LUD-21 verify poller for every Lightning Address Connect connection.
/// </summary>
public sealed class LightningAddressConnectPollerService : IHostedService
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private const int MaxConcurrencyPerCycle = 16;
    private static readonly TimeSpan PersistThrottle = TimeSpan.FromSeconds(10);

    private readonly ILogger<LightningAddressConnectPollerService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeSpan _interval;
    private readonly LightningAddressConnectPersistence? _persistence;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, (int Errors, DateTimeOffset Next)> _backoff = new();
    private Task? _loop;
    private int _lastPersistedVersion;
    private DateTimeOffset _lastPersist = DateTimeOffset.MinValue;

    public LightningAddressConnectPollerService(
        ILogger<LightningAddressConnectPollerService> logger,
        IHttpClientFactory httpClientFactory,
        ISettingsRepository settings)
        : this(logger, httpClientFactory, DefaultInterval, new LightningAddressConnectPersistence(settings)) { }

    internal LightningAddressConnectPollerService(
        ILogger<LightningAddressConnectPollerService> logger,
        IHttpClientFactory httpClientFactory,
        TimeSpan interval,
        LightningAddressConnectPersistence? persistence = null)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _interval = interval;
        _persistence = persistence;
    }

    public Task StartAsync(CancellationToken _)
    {
        _lastPersistedVersion = TrackedInvoiceRegistry.Version;
        _loop = Task.Run(() => Loop(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken _)
    {
        _cts.Cancel();
        if (_loop is not null)
        {
            try { await _loop; } catch { /* cancellation */ }
        }
        if (_persistence != null)
        {
            try { await _persistence.SaveAsync(); }
            catch (Exception e) { _logger.LogDebug(e, "Final Lightning Address Connect persist failed"); }
        }
    }

    private async Task Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TrackedInvoiceRegistry.PruneSettled(DateTimeOffset.UtcNow);
                var invoices = TrackedInvoiceRegistry.Hosts()
                    .SelectMany(TrackedInvoiceRegistry.SnapshotByHost)
                    .ToArray();

                if (!_backoff.IsEmpty)
                {
                    var live = new HashSet<string>(invoices.Select(i => i.PaymentHash));
                    foreach (var key in _backoff.Keys)
                        if (!live.Contains(key))
                            _backoff.TryRemove(key, out _);
                }

                if (invoices.Length > 0)
                {
                    var http = _httpClientFactory.CreateClient(nameof(LightningAddressConnectPollerService));
                    http.Timeout = TimeSpan.FromSeconds(30);
                    using var gate = new SemaphoreSlim(MaxConcurrencyPerCycle);
                    var tasks = invoices.Select(async t =>
                    {
                        await gate.WaitAsync(ct);
                        try { await PollOne(t, http, ct); }
                        finally { gate.Release(); }
                    });
                    await Task.WhenAll(tasks);
                }

                if (_persistence != null)
                {
                    var version = TrackedInvoiceRegistry.Version;
                    var now = DateTimeOffset.UtcNow;
                    if (version != _lastPersistedVersion && now - _lastPersist > PersistThrottle)
                    {
                        try
                        {
                            await _persistence.SaveAsync();
                            _lastPersistedVersion = version;
                            _lastPersist = now;
                        }
                        catch (Exception e)
                        {
                            _logger.LogDebug(e, "Failed to persist Lightning Address Connect tracked invoices");
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception e) { _logger.LogDebug(e, "Lightning Address Connect verify poll cycle error"); }

            try { await Task.Delay(_interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PollOne(TrackedInvoice t, HttpClient http, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (_backoff.TryGetValue(t.PaymentHash, out var b) && now < b.Next) return;
        if (t.ExpiresAt < now)
        {
            TrackedInvoiceRegistry.Remove(t.PaymentHash);
            _backoff.TryRemove(t.PaymentHash, out _);
            return;
        }

        try
        {
            var inv = await LnAddressReceiver.PollAndBuild(t, http, ct);
            _backoff.TryRemove(t.PaymentHash, out _);
            if (inv is null) return;

            if (inv.Status == LightningInvoiceStatus.Paid)
            {
                var pruneAfter = (t.ExpiresAt > DateTimeOffset.UtcNow ? t.ExpiresAt : DateTimeOffset.UtcNow)
                                 + TimeSpan.FromHours(1);
                TrackedInvoiceRegistry.MarkSettled(t.PaymentHash, inv, pruneAfter);
                TrackedInvoiceRegistry.PublishSettled(t, inv);
            }
            else if (inv.Status == LightningInvoiceStatus.Expired)
            {
                TrackedInvoiceRegistry.Remove(t.PaymentHash);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            var errors = (_backoff.TryGetValue(t.PaymentHash, out var prev) ? prev.Errors : 0) + 1;
            var delay = Math.Min(_interval.TotalMilliseconds * Math.Pow(2, errors), MaxBackoff.TotalMilliseconds);
            _backoff[t.PaymentHash] = (errors, DateTimeOffset.UtcNow.AddMilliseconds(delay));
            _logger.LogDebug(e, "Error polling LUD-21 verify for {Hash} (attempt {N})", t.PaymentHash, errors);
        }
    }
}

public sealed class LightningAddressConnectListener : ILightningInvoiceListener
{
    private readonly Channel<LightningInvoice> _channel = Channel.CreateUnbounded<LightningInvoice>();
    private readonly Action<TrackedInvoice, LightningInvoice> _handler;

    public LightningAddressConnectListener(Func<TrackedInvoice, bool> isMine)
    {
        _handler = (t, inv) => { if (isMine(t)) _channel.Writer.TryWrite(inv); };
        TrackedInvoiceRegistry.Settled += _handler;
    }

    public async Task<LightningInvoice> WaitInvoice(CancellationToken ct) => await _channel.Reader.ReadAsync(ct);

    public void Dispose()
    {
        TrackedInvoiceRegistry.Settled -= _handler;
        _channel.Writer.TryComplete();
    }
}
