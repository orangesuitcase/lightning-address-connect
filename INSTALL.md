# Install on your own BTCPay Server

Plugin Builder registration is currently disabled (security hardening). You can still run this plugin on **your** instance.

You need **BTCPay Server ≥ 2.4.2**.

> **About the red “No documentation” flag:** BTCPay only fills Documentation/Source links from the **Plugin Builder catalog**. Sideloaded plugins always show that warning until the plugin is listed there with a Documentation URL. The real docs live at [`docs/README.md`](./docs/README.md) — the flag does not mean the plugin is broken.

## Update to a newer version later

When you sideload an update: bump `<Version>` in the `.csproj`, rebuild on your laptop, replace the plugin folder on the server, restart (same copy steps as install). BTCPay shows the new version in Installed Plugins. The red “No documentation” flag only clears when the plugin is listed on Plugin Builder with a Documentation URL.

## Option A — Copy the packed plugin (fastest if you have the `.btcpay` file)

A packed build is created with:

```bash
dotnet publish BTCPayServer.Plugins.LightningAddressConnect -c Release -o /tmp/lac-publish
dotnet run --project submodules/btcpayserver/BTCPayServer.PluginPacker -- \
  /tmp/lac-publish BTCPayServer.Plugins.LightningAddressConnect /tmp/lac-pkg
```

The file is:

`BTCPayServer.Plugins.LightningAddressConnect.btcpay`

### Docker-based BTCPay (most common)

SSH into your server:

```bash
# Stop BTCPay
cd /root   # or wherever your btcpay scripts live
./btcpay-down.sh

# Plugins live in this Docker volume (path is usually):
PLUGINS=/var/lib/docker/volumes/generated_btcpay_pluginsdir/_data

# Extract the package into a folder named after the plugin
mkdir -p "$PLUGINS/BTCPayServer.Plugins.LightningAddressConnect"
# .btcpay is a zip:
unzip -o BTCPayServer.Plugins.LightningAddressConnect.btcpay \
  -d "$PLUGINS/BTCPayServer.Plugins.LightningAddressConnect"

./btcpay-up.sh
```

Then in the UI: **Server Settings → Plugins** — you should see **Lightning Address Connect**.

### Configure

**Store → Lightning → Settings → Use custom node:**

```
type=lnaddress;ln-address=you@getalby.com
```

Click **Test connection**, then **Save**.

---

## Option B — Build from GitHub on any machine, then copy

```bash
git clone --recursive https://github.com/orangesuitcase/lightning-address-connect.git
cd lightning-address-connect
# needs .NET 10 SDK: https://dot.net
dotnet publish BTCPayServer.Plugins.LightningAddressConnect \
  -c Release -o ./publish

# Copy the publish folder to the server plugins directory as:
#   .../BTCPayServer.Plugins.LightningAddressConnect/
# (must contain BTCPayServer.Plugins.LightningAddressConnect.dll)
```

Then restart BTCPay (`./btcpay-up.sh` or restart the service).

---

## Option C — GitHub Actions artifact

If the workflow in `.github/workflows/pack-plugin.yml` is enabled, open the repo **Actions** tab after a push, download the **plugin** artifact (contains the `.btcpay`), then use Option A.

---

## Uninstall

```bash
./btcpay-down.sh
rm -rf /var/lib/docker/volumes/generated_btcpay_pluginsdir/_data/BTCPayServer.Plugins.LightningAddressConnect
./btcpay-up.sh
```
