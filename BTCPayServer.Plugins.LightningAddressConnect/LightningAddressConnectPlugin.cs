#nullable enable
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Lightning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.LightningAddressConnect;

public class LightningAddressConnectPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    {
        new() { Identifier = nameof(BTCPayServer), Condition = ">=2.4.2" }
    };

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("ln-payment-method-setup-tab", "LightningAddressConnect/LNPaymentMethodSetupTab");
        services.AddSingleton<LightningAddressConnectConnectionStringHandler>();
        services.AddSingleton<ILightningConnectionStringHandler>(sp =>
            sp.GetRequiredService<LightningAddressConnectConnectionStringHandler>());
        services.AddSingleton<LightningAddressConnectPollerService>();
        services.AddSingleton<IHostedService>(sp => sp.GetRequiredService<LightningAddressConnectPollerService>());
        base.Execute(services);
    }
}
