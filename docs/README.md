# Lightning Address Connect

Receive Lightning payments in BTCPay Server by pasting a **Lightning Address**. No node, no API key, no custody by BTCPay.

Payments settle into the wallet behind your address. BTCPay creates BOLT11 invoices via LNURL-pay and detects payment by polling the [LUD-21](https://github.com/lnurl/luds/blob/luds/21.md) `verify` URL.

**Source:** [github.com/orangesuitcase/lightning-address-connect](https://github.com/orangesuitcase/lightning-address-connect)

---

## Requirements

- BTCPay Server **≥ 2.4.2**
- A Lightning Address whose provider supports **LUD-21 verify**

## Quick setup (UI)

1. Install the plugin (Plugin Directory when available, or [manual sideload](../INSTALL.md)).
2. Open **Store → Lightning → Settings**.
3. Choose **Use custom node** and paste:

```text
type=lnaddress;ln-address=you@getalby.com
```

4. Click **Test connection** (BTCPay probes LUD-21; this creates one throwaway invoice on the address).
5. Click **Save**.

Aliases: `address=` or `value=` instead of `ln-address=`.

## Supported wallets

| Wallet | Example | Status |
|--------|---------|--------|
| Alby | `you@getalby.com` | Supported |
| Blink | `you@blink.sv` | Supported (address-only; see note below) |
| LNbits | self-hosted | Supported when LUD-21 is enabled |
| BTCPay Server | any BTCPay host | Supported (LUD-21 on by default) |
| Coinos | `you@coinos.io` | Supported |
| Strike | `you@strike.me` | Not supported yet — use the Strike BTCPay plugin |
| Wallet of Satoshi | `you@walletofsatoshi.com` | Not supported yet |

Any other address that returns a LUD-21 `verify` URL on the LNURL-pay callback also works.

### Blink plugin vs this plugin

Both can be installed together (no conflict). For “paste `you@blink.sv` and receive,” Lightning Address Connect is enough. Keep the Blink plugin if you need custodial API keys, Stablesats/USD options, or sending via Blink.

### Strike and Wallet of Satoshi

Both are excellent Lightning wallets. This plugin isn’t compatible with them **yet**, because settlement detection needs LUD-21 verify on the Lightning Address. For Strike today, use the dedicated **Strike BTCPay plugin** with an API key. We’ll happily add both here once LUD-21 is available on their addresses.

## Greenfield API

API key needs `btcpay.store.canviewstoresettings` (probe) and `btcpay.store.canmodifystoresettings` (configure).

### List known wallets

```http
GET /api/v1/lightning-address-connect/wallets
Authorization: token <api-key>
```

### Probe an address

Creates one throwaway probe invoice (how LUD-21 is discovered).

```http
POST /api/v1/stores/{storeId}/lightning-address-connect/probe
Authorization: token <api-key>
Content-Type: application/json

{ "lightningAddress": "you@getalby.com" }
```

### Configure the store Lightning method

```http
POST /api/v1/stores/{storeId}/lightning-address-connect/configure
Authorization: token <api-key>
Content-Type: application/json

{ "lightningAddress": "you@getalby.com", "enabled": true }
```

### Or use the core payment-methods API

Also runs LUD-21 validation when the connection string changes:

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
- **Amountless / top-up** invoices are not supported
- **Clearnet only** — no Tor `.onion` addresses
- Settlement is **polled** (usually seconds; occasionally up to about a minute)
- Bound by the address `minSendable` / `maxSendable`
- Validating the connection creates one throwaway probe invoice

## Manual install / update

See [INSTALL.md](../INSTALL.md).

## License

MIT — see [LICENSE](../LICENSE).
