#nullable enable
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using Network = NBitcoin.Network;

namespace BTCPayServer.Plugins.LightningAddressConnect;

/// <summary>
/// Creates invoices via LNURL-pay and detects settlement via LUD-21 verify.
/// </summary>
public sealed class LnAddressReceiver
{
    private readonly ResolvedLnAddress _resolved;
    private readonly Network _network;
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public const string VerifyUnsupportedMessage =
        "This Lightning Address does not support the LUD-21 'verify' extension, which is required to detect " +
        "payment settlement without a node or API key. Supported wallets include Alby, Blink, Strike, LNbits, " +
        "and BTCPay. Wallet of Satoshi is not supported until they add LUD-21.";

    public LnAddressReceiver(ResolvedLnAddress resolved, Network network, HttpClient http, ILogger logger)
    {
        _resolved = resolved;
        _network = network;
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Config-time probe: request a minimal invoice and require a LUD-21 verify URL.
    /// Creates one throwaway invoice on the receiver (standard for LUD-21 detection).
    /// </summary>
    public async Task<string?> CheckVerifySupport(CancellationToken ct)
    {
        JObject meta;
        try { meta = await LnAddressResolver.GetJson(_http, _resolved.PayEndpoint, ct); }
        catch (Exception e) { return e.Message; }

        var callback = meta["callback"]?.Value<string>();
        if (string.IsNullOrEmpty(callback)) return "The Lightning Address is missing a LNURL-pay callback URL.";
        var min = meta["minSendable"]?.Value<long>() ?? 1000;

        var cb = new UriBuilder(callback);
        var q = new StringBuilder(cb.Query.TrimStart('?'));
        if (q.Length > 0) q.Append('&');
        q.Append("amount=").Append(min);
        cb.Query = q.ToString();

        JObject json;
        try { json = await LnAddressResolver.GetJson(_http, cb.Uri, ct); }
        catch (Exception e) { return $"Could not request a probe invoice: {e.Message}"; }

        var verify = json["verify"]?.Value<string>();
        if (string.IsNullOrEmpty(verify) || !Uri.TryCreate(verify, UriKind.Absolute, out _))
            return VerifyUnsupportedMessage;
        return null;
    }

    /// <summary>Fetch LNURL-pay metadata (bounds, callback) without creating an invoice.</summary>
    public async Task<JObject> FetchMetadata(CancellationToken ct)
        => await LnAddressResolver.GetJson(_http, _resolved.PayEndpoint, ct);

    public async Task<LightningInvoice> CreateInvoice(LightMoney? amount, string? description,
        CreateInvoiceParams? p, CancellationToken ct)
    {
        if (amount is null || amount.MilliSatoshi <= 0)
            throw new NotSupportedException(
                "Lightning Address Connect requires an invoice amount; amountless/top-up invoices are not supported.");

        var meta = await LnAddressResolver.GetJson(_http, _resolved.PayEndpoint, ct);
        var callback = meta["callback"]?.Value<string>()
                       ?? throw new Exception("LNURL-pay response is missing a callback URL.");
        var min = meta["minSendable"]?.Value<long>() ?? 1;
        var max = meta["maxSendable"]?.Value<long>() ?? long.MaxValue;
        var msat = amount.MilliSatoshi;
        if (msat < min) throw new Exception($"Amount {msat} msat is below the minimum ({min} msat).");
        if (msat > max) throw new Exception($"Amount {msat} msat is above the maximum ({max} msat).");

        var cb = new UriBuilder(callback);
        var q = new StringBuilder(cb.Query.TrimStart('?'));
        if (q.Length > 0) q.Append('&');
        q.Append("amount=").Append(msat);
        // Forward comment only when LUD-12 allows it and Description is not LNURL metadata JSON.
        var commentAllowed = meta["commentAllowed"]?.Value<int>() ?? 0;
        if (commentAllowed > 0 && p is not { DescriptionHashOnly: true } && !string.IsNullOrEmpty(description))
        {
            var c = description!.Length > commentAllowed ? description[..commentAllowed] : description;
            q.Append("&comment=").Append(Uri.EscapeDataString(c));
        }
        cb.Query = q.ToString();

        var json = await LnAddressResolver.GetJson(_http, cb.Uri, ct);
        var pr = json["pr"]?.Value<string>() ?? throw new Exception("LNURL callback did not return an invoice.");
        var bolt11 = BOLT11PaymentRequest.Parse(pr, _network);

        if (bolt11.MinimumAmount != LightMoney.MilliSatoshis(msat))
            throw new Exception(
                $"Lightning Address returned an invoice for {bolt11.MinimumAmount.MilliSatoshi} msat but {msat} was requested.");
        if (p?.DescriptionHash is { } dh && dh != bolt11.DescriptionHash)
            throw new Exception("Lightning Address returned an invoice with a mismatched or missing description hash.");

        var paymentHash = bolt11.PaymentHash?.ToString() ?? throw new Exception("Invoice has no payment hash.");
        var verifyUrl = json["verify"]?.Value<string>();
        if (string.IsNullOrEmpty(verifyUrl) || !Uri.TryCreate(verifyUrl, UriKind.Absolute, out var verifyUri))
            throw new NotSupportedException(VerifyUnsupportedMessage);

        TrackedInvoiceRegistry.Add(new TrackedInvoice(
            paymentHash, pr, verifyUrl, verifyUri.Host, _resolved.PayEndpoint.ToString(), bolt11.ExpiryDate));

        return new LightningInvoice
        {
            Id = paymentHash,
            PaymentHash = paymentHash,
            BOLT11 = pr,
            Amount = bolt11.MinimumAmount,
            Status = LightningInvoiceStatus.Unpaid,
            ExpiresAt = bolt11.ExpiryDate
        };
    }

    public Task<LightningInvoice?> GetInvoice(string paymentHash, CancellationToken ct)
    {
        if (TrackedInvoiceRegistry.TryGetSettled(paymentHash, out var paid))
            return Task.FromResult<LightningInvoice?>(paid);
        if (TrackedInvoiceRegistry.TryGet(paymentHash, out var t))
            return PollAndBuild(t, _http, ct);
        if (TrackedInvoiceRegistry.TryGetSettled(paymentHash, out paid))
            return Task.FromResult<LightningInvoice?>(paid);
        return Task.FromResult<LightningInvoice?>(null);
    }

    public static async Task<LightningInvoice?> PollAndBuild(TrackedInvoice t, HttpClient http, CancellationToken ct)
    {
        JObject? json = null;
        var transportError = false;
        try
        {
            using var resp = await http.GetAsync(t.VerifyUrl, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode) json = JObject.Parse(body);
            else transportError = true;
        }
        catch { transportError = true; }

        if (json?["status"]?.Value<string>()?.Equals("ERROR", StringComparison.OrdinalIgnoreCase) == true)
            return null;

        if (json is null)
        {
            if (transportError)
                return new LightningInvoice
                { Id = t.PaymentHash, PaymentHash = t.PaymentHash, Status = LightningInvoiceStatus.Unpaid };
            return null;
        }

        var settled = json["settled"]?.Value<bool>() ?? false;
        var preimage = json["preimage"]?.Value<string>();
        return BuildInvoice(t, settled, preimage);
    }

    private static LightningInvoice BuildInvoice(TrackedInvoice t, bool settled, string? preimage)
    {
        var bolt11 = BOLT11PaymentRequest.Parse(t.Bolt11, InferNetwork(t.Bolt11));
        var normalizedPreimage = preimage?.Trim();
        string? valid = settled && normalizedPreimage is not null && IsValidPreimage(normalizedPreimage, t.PaymentHash)
            ? normalizedPreimage
            : null;
        var paid = valid is not null;
        var status = paid ? LightningInvoiceStatus.Paid
            : t.ExpiresAt < DateTimeOffset.UtcNow ? LightningInvoiceStatus.Expired
            : LightningInvoiceStatus.Unpaid;
        return new LightningInvoice
        {
            Id = t.PaymentHash,
            PaymentHash = t.PaymentHash,
            BOLT11 = t.Bolt11,
            Amount = bolt11.MinimumAmount,
            AmountReceived = paid ? bolt11.MinimumAmount : null,
            Status = status,
            Preimage = valid,
            PaidAt = paid ? DateTimeOffset.UtcNow : null,
            ExpiresAt = t.ExpiresAt
        };
    }

    public static bool IsValidPreimage(string? preimage, string paymentHash)
    {
        preimage = preimage?.Trim() ?? "";
        paymentHash = paymentHash.Trim().ToLowerInvariant();
        if (preimage.Length != 64 || paymentHash.Length != 64) return false;
        foreach (var c in preimage)
            if (!Uri.IsHexDigit(c)) return false;
        try
        {
            var bytes = Encoders.Hex.DecodeData(preimage);
            var computed = Encoders.Hex.EncodeData(Hashes.SHA256(bytes));
            return computed.Equals(paymentHash, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static Network InferNetwork(string bolt11) =>
        bolt11.StartsWith("lnbcrt", StringComparison.OrdinalIgnoreCase) ? Network.RegTest
        : bolt11.StartsWith("lntb", StringComparison.OrdinalIgnoreCase) ? Network.TestNet
        : Network.Main;
}
