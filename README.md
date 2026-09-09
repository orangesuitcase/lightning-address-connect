# Lightning Address Connect

Connect any **LUD-21-capable Lightning Address** as your BTCPay Server Lightning backend — no node, no API key, no custody by BTCPay.

**Documentation:** [docs/README.md](./docs/README.md)  
**Manual install / update:** [INSTALL.md](./INSTALL.md)

## Connection string

```text
type=lnaddress;ln-address=you@getalby.com
```

In the UI: **Store → Lightning → Settings → Use custom node**, paste the connection string, then **Test connection**.

## Supported wallets (summary)

Alby, Blink (`@blink.sv`), LNbits, BTCPay, Coinos — Strike and Wallet of Satoshi not supported yet. Full table and API docs: [docs/README.md](./docs/README.md).

## Development

```bash
git submodule update --init --recursive
dotnet build BTCPayServer.Plugins.LightningAddressConnect/BTCPayServer.Plugins.LightningAddressConnect.csproj -c Release
```

See [PUBLISHING.md](./PUBLISHING.md) for Plugin Builder release steps (when registration is available again). When listing there, set **Documentation URL** to:

`https://github.com/orangesuitcase/lightning-address-connect/blob/main/docs/README.md`
