#nullable enable
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.LightningAddressConnect.Models;

public class ProbeLightningAddressRequest
{
    /// <summary>Lightning Address to probe, e.g. you@getalby.com</summary>
    [Required]
    public string LightningAddress { get; set; } = "";
}

public class ProbeLightningAddressResponse
{
    public bool Compatible { get; set; }
    public string LightningAddress { get; set; } = "";
    public string? Domain { get; set; }
    public string? Error { get; set; }
    public string? KnownWallet { get; set; }
    public bool? KnownWalletSupportsLud21 { get; set; }
    public long? MinSendableMsat { get; set; }
    public long? MaxSendableMsat { get; set; }
    public string? ConnectionString { get; set; }
    public string? Notes { get; set; }
}

public class SupportedWalletDto
{
    public string Name { get; set; } = "";
    public string Domain { get; set; } = "";
    public bool Lud21Supported { get; set; }
    public string Notes { get; set; } = "";
}

public class SupportedWalletsResponse
{
    public List<SupportedWalletDto> Wallets { get; set; } = new();
}

public class ConfigureLightningAddressRequest
{
    /// <summary>Lightning Address, e.g. you@getalby.com</summary>
    [Required]
    public string LightningAddress { get; set; } = "";

    /// <summary>When true (default), enables the BTC-LN payment method after configuring.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Crypto code, default BTC.</summary>
    public string CryptoCode { get; set; } = "BTC";

    /// <summary>When true (default), run the LUD-21 probe before saving.</summary>
    public bool ValidateBeforeSave { get; set; } = true;
}

public class ConfigureLightningAddressResponse
{
    public bool Success { get; set; }
    public string? ConnectionString { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? Error { get; set; }
    public ProbeLightningAddressResponse? Probe { get; set; }
}
