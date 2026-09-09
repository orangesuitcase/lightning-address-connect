#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Plugins.LightningAddressConnect;

/// <summary>
/// Persists tracked invoices so payment detection survives a graceful BTCPay restart.
/// </summary>
public sealed class LightningAddressConnectPersistence
{
    public const string SettingName = "LightningAddressConnect.TrackedInvoices";
    private readonly ISettingsRepository _settings;

    public LightningAddressConnectPersistence(ISettingsRepository settings) => _settings = settings;

    public async Task SaveAsync()
    {
        var snapshot = new PersistedTrackedInvoices
        {
            Invoices = TrackedInvoiceRegistry.All().Select(t => new PersistedInvoice
            {
                PaymentHash = t.PaymentHash,
                Bolt11 = t.Bolt11,
                VerifyUrl = t.VerifyUrl,
                VerifyHost = t.VerifyHost,
                PayEndpoint = t.PayEndpoint,
                ExpiresAtUnix = t.ExpiresAt.ToUnixTimeSeconds()
            }).ToList()
        };
        await _settings.UpdateSetting(snapshot, SettingName);
    }

    public async Task LoadAsync()
    {
        var persisted = await _settings.GetSettingAsync<PersistedTrackedInvoices>(SettingName);
        if (persisted?.Invoices is null) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var p in persisted.Invoices)
        {
            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(p.ExpiresAtUnix);
            if (expiresAt <= now) continue;
            if (string.IsNullOrEmpty(p.PaymentHash) || string.IsNullOrEmpty(p.VerifyUrl)) continue;
            TrackedInvoiceRegistry.Add(new TrackedInvoice(
                p.PaymentHash, p.Bolt11, p.VerifyUrl, p.VerifyHost, p.PayEndpoint, expiresAt));
        }
    }
}

public class PersistedTrackedInvoices
{
    public List<PersistedInvoice> Invoices { get; set; } = new();
}

public class PersistedInvoice
{
    public string PaymentHash { get; set; } = "";
    public string Bolt11 { get; set; } = "";
    public string VerifyUrl { get; set; } = "";
    public string VerifyHost { get; set; } = "";
    public string PayEndpoint { get; set; } = "";
    public long ExpiresAtUnix { get; set; }
}
