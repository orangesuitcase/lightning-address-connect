# Publishing Lightning Address Connect

This walkthrough covers releasing the plugin via the [BTCPay Server Plugin Builder](https://plugin-builder.btcpayserver.org/).

## 1. Put the code on a public Git host

The Builder must clone your repo over HTTPS **without credentials**.

1. Create a public GitHub (or GitLab) repository, e.g. `lightning-address-connect`.
2. Push this project (including `.gitmodules`).
3. Ensure the `submodules/btcpayserver` submodule points at a public URL (`https://github.com/btcpayserver/btcpayserver.git`).

```bash
cd lightning-address-connect
git submodule update --init --recursive
git remote add origin https://github.com/<you>/lightning-address-connect.git
git push -u origin main
```

The Builder clones submodules recursively. Your `.csproj` must keep the `ProjectReference` to `..\submodules\btcpayserver\BTCPayServer\BTCPayServer.csproj` (or adjust `pluginDirectory` so that path resolves).

## 2. Bump the version before each release

In `BTCPayServer.Plugins.LightningAddressConnect.csproj`:

```xml
<Version>1.0.1</Version>
```

Increment for every published build (`1.0.1`, `1.1.0`, …). The Builder packages whatever `<Version>` is in the project file.

## 3. Create the plugin on Plugin Builder

1. Sign in at [plugin-builder.btcpayserver.org](https://plugin-builder.btcpayserver.org/).
2. **Create Plugin**
   - **Slug:** e.g. `lightning-address-connect`
   - **Title:** Lightning Address Connect
   - **Description:** short summary (same idea as the csproj `Description`)
3. **Create Build**
   - **Git repository:** `https://github.com/<you>/lightning-address-connect.git`
   - **Branch/tag:** `main` (or a release tag)
   - **Plugin directory:** `BTCPayServer.Plugins.LightningAddressConnect`  
     (folder that contains the single `.csproj`)
   - **Build configuration:** `Release` (default)
4. Submit and watch the build log. On success the version is published as a **pre-release**.

## 4. Install the pre-release on a test BTCPay

1. On your BTCPay: **Server Settings → Plugin Directory** (or **Manage Plugins**).
2. Search by the **exact plugin name** if it is still unlisted.
3. Install the pre-release, **restart** when prompted.
4. Smoke-test:
   - Setup tab appears under Lightning custom node
   - `type=lnaddress;ln-address=you@getalby.com` → Test connection succeeds
   - `you@walletofsatoshi.com` → Test connection explains it is not supported yet
   - Create a store invoice, pay it, confirm it settles
   - Greenfield: `POST .../lightning-address-connect/probe` and `.../configure`

## 5. Request a public listing

After the pre-release looks good:

1. In Plugin Builder, request that the plugin be **listed** publicly.
2. Listing usually requires a **logo** (and often a short demo video).
3. Once listed, users find it in the in-app plugin directory by name.

For later versions: bump `<Version>`, push, create another build, test the pre-release, then release/list that version.

## 6. Optional local pack (without Builder)

If you have a full BTCPay checkout with `BTCPayServer.PluginPacker`:

```bash
dotnet publish BTCPayServer.Plugins.LightningAddressConnect \
  -c Release -o /tmp/lac-build
dotnet run --project /path/to/btcpayserver/BTCPayServer.PluginPacker -- \
  /tmp/lac-build BTCPayServer.Plugins.LightningAddressConnect /tmp/lac-pkg
```

Prefer the official Builder for anything you distribute.

## Checklist

- [ ] Public git repo + working `btcpayserver` submodule
- [ ] `<Version>` bumped
- [ ] Builder build green (pre-release)
- [ ] Manual test on a real BTCPay instance
- [ ] Logo (and demo if required) for listing
- [ ] Listing requested / approved
