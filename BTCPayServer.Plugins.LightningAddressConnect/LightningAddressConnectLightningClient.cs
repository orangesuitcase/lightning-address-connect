#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Network = NBitcoin.Network;

namespace BTCPayServer.Plugins.LightningAddressConnect;

/// <summary>
/// Receive-only Lightning backend backed by a Lightning Address (LNURL-pay + LUD-21).
/// </summary>
public sealed class LightningAddressConnectLightningClient : IExtendedLightningClient
{
    private readonly ResolvedLnAddress _resolved;
    private readonly LnAddressReceiver _receiver;

    private const string ReceiveOnlyMsg =
        "Lightning Address Connect is receive-only. Sending, balance and channel operations are not available.";

    public LightningAddressConnectLightningClient(
        ResolvedLnAddress resolved, Network network, HttpClient http, ILoggerFactory lf)
    {
        _resolved = resolved;
        _receiver = new LnAddressReceiver(resolved, network, http, lf.CreateLogger(nameof(LnAddressReceiver)));
    }

    public Task<LightningInvoice> CreateInvoice(LightMoney amount, string description, TimeSpan expiry,
        CancellationToken cancellation = default)
        => _receiver.CreateInvoice(amount, description, null, cancellation);

    public Task<LightningInvoice> CreateInvoice(CreateInvoiceParams createInvoiceRequest,
        CancellationToken cancellation = default)
        => _receiver.CreateInvoice(createInvoiceRequest.Amount, createInvoiceRequest.Description,
            createInvoiceRequest, cancellation);

    public Task<LightningInvoice?> GetInvoice(string invoiceId, CancellationToken cancellation = default)
        => _receiver.GetInvoice(invoiceId, cancellation);

    public Task<LightningInvoice?> GetInvoice(uint256 paymentHash, CancellationToken cancellation = default)
        => _receiver.GetInvoice(paymentHash.ToString(), cancellation);

    public Task<LightningInvoice[]> ListInvoices(CancellationToken cancellation = default)
        => ListInvoices(new ListInvoicesParams(), cancellation);

    public Task<LightningInvoice[]> ListInvoices(ListInvoicesParams request, CancellationToken cancellation = default)
    {
        var mine = TrackedInvoiceRegistry.All()
            .Where(t => t.PayEndpoint == _resolved.PayEndpoint.ToString())
            .Select(t => new LightningInvoice
            {
                Id = t.PaymentHash,
                PaymentHash = t.PaymentHash,
                BOLT11 = t.Bolt11,
                Status = LightningInvoiceStatus.Unpaid,
                ExpiresAt = t.ExpiresAt
            })
            .Where(i => request?.PendingOnly is not true || i.Status == LightningInvoiceStatus.Unpaid)
            .ToArray();
        return Task.FromResult(mine);
    }

    public Task<ILightningInvoiceListener> Listen(CancellationToken cancellation = default)
        => Task.FromResult<ILightningInvoiceListener>(
            new LightningAddressConnectListener(t => t.PayEndpoint == _resolved.PayEndpoint.ToString()));

    public Task CancelInvoice(string invoiceId, CancellationToken cancellation = default)
    {
        TrackedInvoiceRegistry.Remove(invoiceId);
        return Task.CompletedTask;
    }

    public Task<PayResponse> Pay(string bolt11, PayInvoiceParams payParams, CancellationToken cancellation = default)
        => throw new NotSupportedException(ReceiveOnlyMsg);

    public Task<PayResponse> Pay(string bolt11, CancellationToken cancellation = default)
        => throw new NotSupportedException(ReceiveOnlyMsg);

    public Task<PayResponse> Pay(PayInvoiceParams payParams, CancellationToken cancellation = default)
        => throw new NotSupportedException(ReceiveOnlyMsg);

    public Task<LightningNodeBalance> GetBalance(CancellationToken cancellation = default)
        => throw new NotSupportedException(ReceiveOnlyMsg);

    public Task<LightningPayment?> GetPayment(string paymentHash, CancellationToken cancellation = default)
        => Task.FromResult<LightningPayment?>(null);

    public Task<LightningPayment[]> ListPayments(CancellationToken cancellation = default)
        => Task.FromResult(Array.Empty<LightningPayment>());

    public Task<LightningPayment[]> ListPayments(ListPaymentsParams request, CancellationToken cancellation = default)
        => Task.FromResult(Array.Empty<LightningPayment>());

    public Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default)
        => throw new NotSupportedException(ReceiveOnlyMsg);

    public Task<BitcoinAddress> GetDepositAddress(CancellationToken cancellation = default)
        => throw new NotSupportedException(ReceiveOnlyMsg);

    public Task<OpenChannelResponse> OpenChannel(OpenChannelRequest openChannelRequest,
        CancellationToken cancellation = default)
        => throw new NotSupportedException(ReceiveOnlyMsg);

    public Task<ConnectionResult> ConnectTo(NodeInfo nodeInfo, CancellationToken cancellation = default)
        => throw new NotSupportedException(ReceiveOnlyMsg);

    public Task<LightningChannel[]> ListChannels(CancellationToken cancellation = default)
        => throw new NotSupportedException(ReceiveOnlyMsg);

    public async Task<ValidationResult?> Validate()
    {
        var err = await _receiver.CheckVerifySupport(CancellationToken.None);
        return err is null ? ValidationResult.Success : new ValidationResult(err);
    }

    public string? DisplayName => $"Lightning Address ({_resolved.LightningAddress})";
    public Uri? ServerUri => new($"https://{_resolved.Domain}");
}
