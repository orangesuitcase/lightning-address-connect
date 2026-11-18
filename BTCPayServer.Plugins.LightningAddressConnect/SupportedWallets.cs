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

    /// <summary>Major wallets we have checked. Some are not supported yet pending LUD-21.</summary>
    public static IReadOnlyList<WalletInfo> All { get; } = new[]
    {
        new WalletInfo("Alby", "getalby.com", true,
            "Custodial Alby addresses support LUD-21 (including crowdfund via store LNURL). Alby Hub users can also connect via the Nostr (NWC) plugin."),
        new WalletInfo("Blink", "blink.sv", true,
            "Works with Lightning Address Connect (including crowdfund via LNURL). Blink’s dedicated plugin also supports amountless BOLT11, Stablesats, and API key mode."),
        new WalletInfo("Speed", "speed.app", true,
            "Speed Lightning Addresses return LUD-21 verify; crowdfund works when store LNURL is enabled."),
        new WalletInfo("Stacker News", "stacker.news", true,
            "stacker.news Lightning Addresses return LUD-21 verify."),
        new WalletInfo("Strike", "strike.me", false,
            "Not supported yet — Strike LNURL-pay does not return LUD-21 verify (re-checked; still missing). Use the dedicated Strike BTCPay plugin (API key). Address support here needs Strike to add verify."),
        new WalletInfo("LNbits", "*", true,
            "Self-hosted LNbits Lightning Addresses expose LUD-21 when enabled on the instance."),
        new WalletInfo("BTCPay Server", "*", true,
            "Another BTCPay store's Lightning Address works as a receive backend (LUD-21 enabled by default)."),
        new WalletInfo("Coinos", "coinos.io", false,
            "Not supported right now — Coinos LNURL-pay callbacks no longer return LUD-21 verify (needed for settlement detection). Re-enable here if Coinos restores verify."),
        new WalletInfo("Wallet of Satoshi", "walletofsatoshi.com", false,
            "Not supported yet — Wallet of Satoshi LNURL-pay does not return LUD-21 verify. We’ll add it as soon as they offer verify."),
        new WalletInfo("ZBD", "zbd.gg", false,
            "Not supported — ZBD LNURL-pay does not return LUD-21 verify."),
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
