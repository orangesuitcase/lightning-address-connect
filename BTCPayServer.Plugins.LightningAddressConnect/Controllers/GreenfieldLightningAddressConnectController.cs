#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Plugins.LightningAddressConnect.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.LightningAddressConnect.Controllers;

/// <summary>
/// Greenfield API for probing and configuring Lightning Address Connect.
/// Setting the payment method also works via the core endpoint
/// <c>PUT /api/v1/stores/{storeId}/payment-methods/BTC-LN</c> with
/// <c>{"enabled":true,"config":{"connectionString":"type=lnaddress;ln-address=you@getalby.com"}}</c>
/// — that path already runs LUD-21 validation through <see cref="IExtendedLightningClient.Validate"/>.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[EnableCors(CorsPolicies.All)]
public class GreenfieldLightningAddressConnectController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;

    public GreenfieldLightningAddressConnectController(
        IHttpClientFactory httpClientFactory,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers)
    {
        _httpClientFactory = httpClientFactory;
        _storeRepository = storeRepository;
        _handlers = handlers;
    }

    /// <summary>List known wallet compatibility (including wallets not supported yet).</summary>
    [HttpGet("~/api/v1/lightning-address-connect/wallets")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public IActionResult GetSupportedWallets()
    {
        return Ok(new SupportedWalletsResponse
        {
            Wallets = SupportedWallets.All.Select(w => new SupportedWalletDto
            {
                Name = w.Name,
                Domain = w.Domain,
                Lud21Supported = w.Lud21Supported,
                Notes = w.Notes
            }).ToList()
        });
    }

    /// <summary>
    /// Probe a Lightning Address for LNURL-pay + LUD-21 verify support.
    /// Creates one throwaway probe invoice on the address (required to discover verify).
    /// </summary>
    [HttpPost("~/api/v1/stores/{storeId}/lightning-address-connect/probe")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> Probe(string storeId, [FromBody] ProbeLightningAddressRequest? request,
        CancellationToken cancellationToken)
    {
        if (HttpContext.GetStoreData() is null)
            return this.CreateAPIError(404, "store-not-found", "Store was not found");

        if (request is null || string.IsNullOrWhiteSpace(request.LightningAddress))
        {
            ModelState.AddModelError(nameof(ProbeLightningAddressRequest.LightningAddress), "LightningAddress is required");
            return this.CreateValidationError(ModelState);
        }

        var result = await ProbeAddressAsync(request.LightningAddress.Trim(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Probe then set the store's Lightning payment method to the Lightning Address connection string.
    /// </summary>
    [HttpPost("~/api/v1/stores/{storeId}/lightning-address-connect/configure")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield, Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> Configure(string storeId, [FromBody] ConfigureLightningAddressRequest? request,
        CancellationToken cancellationToken)
    {
        var store = HttpContext.GetStoreData();
        if (store is null)
            return this.CreateAPIError(404, "store-not-found", "Store was not found");

        if (request is null || string.IsNullOrWhiteSpace(request.LightningAddress))
        {
            ModelState.AddModelError(nameof(ConfigureLightningAddressRequest.LightningAddress), "LightningAddress is required");
            return this.CreateValidationError(ModelState);
        }

        var address = request.LightningAddress.Trim();
        var cryptoCode = string.IsNullOrWhiteSpace(request.CryptoCode)
            ? "BTC"
            : request.CryptoCode.Trim().ToUpperInvariant();
        var probe = await ProbeAddressAsync(address, cancellationToken);

        if (request.ValidateBeforeSave && !probe.Compatible)
        {
            return Ok(new ConfigureLightningAddressResponse
            {
                Success = false,
                Error = probe.Error ?? "Lightning Address is not compatible.",
                Probe = probe
            });
        }

        var connectionString = probe.ConnectionString
                               ?? LightningAddressConnectConnectionStringHandler.BuildConnectionString(address);
        var pmi = PaymentTypes.LN.GetPaymentMethodId(cryptoCode);

        if (!_handlers.TryGetValue(pmi, out var handler) || handler is not LightningLikePaymentHandler lnHandler)
        {
            return this.CreateAPIError(400, "payment-method-unavailable",
                $"Lightning payment method '{pmi}' is not available on this server.");
        }

        var conf = new LightningPaymentMethodConfig { ConnectionString = connectionString };
        try
        {
            var client = lnHandler.CreateLightningClient(conf);
            if (client is IExtendedLightningClient ext)
            {
                var validation = await ext.Validate();
                if (validation != System.ComponentModel.DataAnnotations.ValidationResult.Success &&
                    validation?.ErrorMessage is { } msg)
                {
                    return Ok(new ConfigureLightningAddressResponse
                    {
                        Success = false,
                        Error = msg,
                        ConnectionString = connectionString,
                        Probe = probe
                    });
                }
            }
        }
        catch (Exception e)
        {
            return Ok(new ConfigureLightningAddressResponse
            {
                Success = false,
                Error = e.Message,
                ConnectionString = connectionString,
                Probe = probe
            });
        }

        store.SetPaymentMethodConfig(lnHandler, conf);
        var blob = store.GetStoreBlob();
        blob.SetExcluded(pmi, !request.Enabled);
        store.SetStoreBlob(blob);
        await _storeRepository.UpdateStore(store);

        return Ok(new ConfigureLightningAddressResponse
        {
            Success = true,
            ConnectionString = connectionString,
            PaymentMethodId = pmi.ToString(),
            Probe = probe
        });
    }

    private async Task<ProbeLightningAddressResponse> ProbeAddressAsync(string lightningAddress, CancellationToken ct)
    {
        var known = SupportedWallets.Match(lightningAddress);
        var response = new ProbeLightningAddressResponse
        {
            LightningAddress = lightningAddress,
            KnownWallet = known?.Name,
            KnownWalletSupportsLud21 = known?.Lud21Supported,
            Notes = known?.Notes,
            ConnectionString = LightningAddressConnectConnectionStringHandler.BuildConnectionString(lightningAddress)
        };

        if (known is { Lud21Supported: false })
        {
            response.Compatible = false;
            response.Domain = known.Domain;
            response.Error = known.Notes;
            return response;
        }

        var http = _httpClientFactory.CreateClient(nameof(GreenfieldLightningAddressConnectController));
        http.Timeout = TimeSpan.FromSeconds(30);

        try
        {
            var resolved = await LnAddressResolver.Resolve(lightningAddress, http, ct);
            response.Domain = resolved.Domain;
            response.LightningAddress = resolved.LightningAddress;
            response.ConnectionString =
                LightningAddressConnectConnectionStringHandler.BuildConnectionString(resolved.LightningAddress);

            var meta = await LnAddressResolver.GetJson(http, resolved.PayEndpoint, ct);
            response.MinSendableMsat = meta["minSendable"]?.Value<long>();
            response.MaxSendableMsat = meta["maxSendable"]?.Value<long>();

            var receiver = new LnAddressReceiver(resolved, NBitcoin.Network.Main, http,
                NullLogger.Instance);
            var err = await receiver.CheckVerifySupport(ct);
            if (err is not null)
            {
                response.Compatible = false;
                response.Error = err;
                return response;
            }

            response.Compatible = true;
            return response;
        }
        catch (Exception e)
        {
            response.Compatible = false;
            response.Error = e.Message;
            return response;
        }
    }
}
