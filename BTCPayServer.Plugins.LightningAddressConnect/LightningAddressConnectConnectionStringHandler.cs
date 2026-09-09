#nullable enable
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using Microsoft.Extensions.Logging;
using Network = NBitcoin.Network;

namespace BTCPayServer.Plugins.LightningAddressConnect;

public class LightningAddressConnectConnectionStringHandler : ILightningConnectionStringHandler
{
    public const string ConnectionType = "lnaddress";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ISettingsRepository? _settings;

    private static readonly ConcurrentDictionary<string, (ResolvedLnAddress Resolved, DateTimeOffset Expiry)> _cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private static readonly object _loadLock = new();
    private static bool _loaded;

    public LightningAddressConnectConnectionStringHandler(
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        ISettingsRepository? settings = null)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _settings = settings;
    }

    public ILightningClient? Create(string connectionString, Network network, out string? error)
    {
        var kv = LightningConnectionStringHelper.ExtractValues(connectionString, out var type);
        if (!string.Equals(type, ConnectionType, StringComparison.OrdinalIgnoreCase))
        {
            error = null;
            return null;
        }

        if (!TryGetAddress(kv, out var address) || string.IsNullOrWhiteSpace(address))
        {
            error =
                "The key 'ln-address' (or 'address') is mandatory. Example: type=lnaddress;ln-address=you@getalby.com";
            return null;
        }

        EnsurePersistedInvoicesLoaded();

        error = null;
        var http = _httpClientFactory.CreateClient(nameof(LightningAddressConnectConnectionStringHandler));
        http.Timeout = TimeSpan.FromSeconds(30);

        ResolvedLnAddress resolved;
        if (_cache.TryGetValue(address, out var cached) && cached.Expiry > DateTimeOffset.UtcNow)
        {
            resolved = cached.Resolved;
        }
        else
        {
            try
            {
                resolved = LnAddressResolver.Resolve(address, http, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }
            _cache[address] = (resolved, DateTimeOffset.UtcNow.Add(CacheTtl));
        }

        return new LightningAddressConnectLightningClient(resolved, network, http, _loggerFactory);
    }

    internal static bool TryGetAddress(System.Collections.Generic.Dictionary<string, string> kv, out string? address)
    {
        if (kv.TryGetValue("ln-address", out address) && !string.IsNullOrWhiteSpace(address))
            return true;
        if (kv.TryGetValue("address", out address) && !string.IsNullOrWhiteSpace(address))
            return true;
        // Alias used by some docs / LNURL plugins
        if (kv.TryGetValue("value", out address) && !string.IsNullOrWhiteSpace(address))
            return true;
        address = null;
        return false;
    }

    /// <summary>Build a connection string from a Lightning Address (for API helpers).</summary>
    public static string BuildConnectionString(string lightningAddress)
        => $"type={ConnectionType};ln-address={lightningAddress.Trim()}";

    private void EnsurePersistedInvoicesLoaded()
    {
        if (_loaded || _settings is null) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            try
            {
                new LightningAddressConnectPersistence(_settings).LoadAsync().GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                _loggerFactory.CreateLogger(nameof(LightningAddressConnectConnectionStringHandler))
                    .LogWarning(e, "Failed to load persisted Lightning Address Connect tracked invoices");
            }
            _loaded = true;
        }
    }
}
