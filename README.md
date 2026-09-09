# Lightning Address Connect

Connect any **LUD-21-capable Lightning Address** as your BTCPay Server Lightning backend — no node, no API key, no custody by BTCPay.

Payments settle straight into the wallet behind your address (e.g. Alby). BTCPay creates BOLT11 invoices via LNURL-pay and detects payment by polling the LUD-21 `verify` URL.

## Connection string

```
type=lnaddress;ln-address=you@getalby.com
```

Aliases: `address=` or `value=` instead of `ln-address=`.

In the UI: **Store → Lightning → Settings → Use custom node**, paste the connection string, then **Test connection** (this probes LUD-21).

## Supported wallets

| Wallet | Domain | Status |
|--------|--------|--------|
| **Alby** | `getalby.com` | Supported |
| **Blink** | `blink.sv` | Supported (or use the Blink plugin) |
| **Strike** | `strike.me` | Supported |
| **LNbits** | self-hosted | Supported when LUD-21 is enabled |
| **BTCPay Server** | any BTCPay host | Supported (LUD-21 on by default) |
| **Coinos** | `coinos.io` | Supported |
| **Wallet of Satoshi** | `walletofsatoshi.com` | **Not supported** — no LUD-21 verify, no merchant API |

Any other Lightning Address that returns a LUD-21 `verify` URL on the LNURL-pay callback also works. Addresses that do not are rejected at setup.

### Why Wallet of Satoshi is missing

WoS can create invoices via LNURL-pay, but without LUD-21 (or an API) BTCPay has no way to know an invoice was paid. Please ask Wallet of Satoshi to add [LUD-21](https://github.com/lnurl/luds/blob/luds/21.md).

## Greenfield API

API key needs `btcpay.store.canviewstoresettings` (probe / wallets) and `btcpay.store.canmodifystoresettings` (configure).

### List known wallets

```http
GET /api/v1/lightning-address-connect/wallets
Authorization: token <api-key>
```

### Probe an address (LUD-21 check)

Creates one throwaway probe invoice on the address (how LUD-21 is discovered).

```http
POST /api/v1/stores/{storeId}/lightning-address-connect/probe
Authorization: token <api-key>
Content-Type: application/json

{ "lightningAddress": "you@getalby.com" }
```

Example success:

```json
{
  "compatible": true,
  "lightningAddress": "you@getalby.com",
  "domain": "getalby.com",
  "knownWallet": "Alby",
  "knownWalletSupportsLud21": true,
  "minSendableMsat": 1000,
  "maxSendableMsat": 10000000000,
  "connectionString": "type=lnaddress;ln-address=you@getalby.com"
}
```

### Configure the store Lightning method

```http
POST /api/v1/stores/{storeId}/lightning-address-connect/configure
Authorization: token <api-key>
Content-Type: application/json

{ "lightningAddress": "you@getalby.com", "enabled": true }
```

### Or use the core payment-methods API

This also runs LUD-21 validation when the connection string changes:

```http
PUT /api/v1/stores/{storeId}/payment-methods/BTC-LN
Authorization: token <api-key>
Content-Type: application/json

{
  "enabled": true,
  "config": {
    "connectionString": "type=lnaddress;ln-address=you@getalby.com"
  }
}
```

## Limitations

- **Receive-only** — no send, balance, or channels
- **Amountless / top-up** invoices are not supported (LNURL-pay is amount-driven)
- **Clearnet only** — no Tor `.onion` Lightning Addresses
- Settlement is **polled** (usually seconds; occasionally up to about a minute)
- Bound by the address `minSendable` / `maxSendable`
- Validating the connection creates one throwaway probe invoice

## Requirements

- BTCPay Server **≥ 2.4.2**
- Plugin identifier: `BTCPayServer.Plugins.LightningAddressConnect`

## Development

```bash
git submodule update --init --recursive
dotnet build BTCPayServer.Plugins.LightningAddressConnect/BTCPayServer.Plugins.LightningAddressConnect.csproj -c Release
```

See [PUBLISHING.md](./PUBLISHING.md) for Plugin Builder release steps.
