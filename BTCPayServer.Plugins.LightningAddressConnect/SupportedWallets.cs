#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BTCPayServer.Plugins.LightningAddressConnect;

/// <summary>
/// Known Lightning Address providers and whether they advertise LUD-21 verify
/// (required for BTCPay to detect settlement without a node or API key).
/// </summary>
public static class SupportedWallets
{
    public sealed record WalletInfo(
        string Name,
        string Domain,
        bool Lud21Supported,
        string Notes);

    /// <summary>Major wallets we have verified. Wallet of Satoshi is intentionally listed as unsupported.</summary>
    public static IReadOnlyList<WalletInfo> All { get; } = new[]
    {
        new WalletInfo("Alby", "getalby.com", true,
            "Custodial Alby addresses support LUD-21. Alby Hub users can also connect via the Nostr (NWC) plugin."),
        new WalletInfo("Blink", "blink.sv", true,
            "Works with Lightning Address Connect. Blink also ships a dedicated plugin with extra features (Stablesats, API key mode)."),
        new WalletInfo("Strike", "strike.me", true,
            "LNURL-pay + LUD-21. Strike also has a dedicated BTCPay plugin for full API access."),
        new WalletInfo("LNbits", "*", true,
            "Self-hosted LNbits Lightning Addresses expose LUD-21 when enabled on the instance."),
        new WalletInfo("BTCPay Server", "*", true,
            "Another BTCPay store's Lightning Address works as a receive backend (LUD-21 enabled by default)."),
        new WalletInfo("Coinos", "coinos.io", true,
            "Supports LNURL-pay with verify for settlement detection."),
        new WalletInfo("Wallet of Satoshi", "walletofsatoshi.com", false,
            "Not supported: LNURL-pay works, but Wallet of Satoshi does not advertise LUD-21 verify and offers no merchant API. Ask them to add LUD-21."),
    };

    public static WalletInfo? Match(string lightningAddress)
    {
        if (string.IsNullOrWhiteSpace(lightningAddress) || !lightningAddress.Contains('@'))
            return null;
        var domain = lightningAddress.Trim().Split('@')[^1].ToLowerInvariant();
        return All.FirstOrDefault(w =>
            w.Domain != "*" && domain.Equals(w.Domain, StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<WalletInfo> Supported => All.Where(w => w.Lud21Supported);
    public static IEnumerable<WalletInfo> Unsupported => All.Where(w => !w.Lud21Supported);
}
