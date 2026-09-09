#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BTCPayServer.Lightning;

namespace BTCPayServer.Plugins.LightningAddressConnect;

public sealed record TrackedInvoice(
    string PaymentHash,
    string Bolt11,
    string VerifyUrl,
    string VerifyHost,
    string PayEndpoint,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Static registry shared across client instances. BTCPay creates separate ILightningClient
/// instances for invoice creation vs payment listening — per-instance state would be invisible
/// to the listener.
/// </summary>
public static class TrackedInvoiceRegistry
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TrackedInvoice>> _byHost = new();
    private static readonly ConcurrentDictionary<string, string> _hostOf = new();
    private static readonly ConcurrentDictionary<string, (LightningInvoice Invoice, DateTimeOffset PruneAfter)> _settled = new();

    public static event Action<TrackedInvoice, LightningInvoice>? Settled;

    private static int _version;
    public static int Version => Volatile.Read(ref _version);

    public static void Add(TrackedInvoice t)
    {
        _byHost.GetOrAdd(t.VerifyHost, _ => new()).AddOrUpdate(t.PaymentHash, t, (_, _) => t);
        _hostOf[t.PaymentHash] = t.VerifyHost;
        Interlocked.Increment(ref _version);
    }

    public static bool TryGet(string paymentHash, out TrackedInvoice t)
    {
        t = default!;
        return _hostOf.TryGetValue(paymentHash, out var host)
               && _byHost.TryGetValue(host, out var inner)
               && inner.TryGetValue(paymentHash, out t!);
    }

    public static void Remove(string paymentHash)
    {
        if (!_hostOf.TryRemove(paymentHash, out var host)) return;
        if (_byHost.TryGetValue(host, out var inner))
            inner.TryRemove(paymentHash, out _);
        Interlocked.Increment(ref _version);
    }

    public static void MarkSettled(string paymentHash, LightningInvoice paid, DateTimeOffset pruneAfter)
    {
        _settled[paymentHash] = (paid, pruneAfter);
        Remove(paymentHash);
    }

    public static bool TryGetSettled(string paymentHash, out LightningInvoice invoice)
    {
        invoice = default!;
        if (!_settled.TryGetValue(paymentHash, out var e)) return false;
        if (e.PruneAfter < DateTimeOffset.UtcNow)
        {
            _settled.TryRemove(paymentHash, out _);
            return false;
        }
        invoice = e.Invoice;
        return true;
    }

    public static void PruneSettled(DateTimeOffset now)
    {
        foreach (var kv in _settled)
            if (kv.Value.PruneAfter < now)
                _settled.TryRemove(kv.Key, out _);
    }

    public static IReadOnlyCollection<TrackedInvoice> SnapshotByHost(string host) =>
        _byHost.TryGetValue(host, out var inner) ? inner.Values.ToArray() : Array.Empty<TrackedInvoice>();

    public static IReadOnlyCollection<string> Hosts() => _byHost.Keys.ToArray();

    public static IReadOnlyCollection<TrackedInvoice> All() =>
        _byHost.Values.SelectMany(v => v.Values).ToArray();

    public static void PublishSettled(TrackedInvoice t, LightningInvoice inv) => Settled?.Invoke(t, inv);
}
