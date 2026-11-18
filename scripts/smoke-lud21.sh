#!/usr/bin/env bash
# Smoke-test LNURL-pay + LUD-21 for sample addresses (no BTCPay required).
# Exit 0 even when unsupported wallets lack verify — those are expected failures.
set -euo pipefail

probe() {
  local expect_verify="$1" # yes|no
  local addr="$2"
  local user="${addr%@*}"
  local domain="${addr#*@}"
  echo "=== $addr (expect lud21=$expect_verify) ==="
  local meta
  # -L: some hosts (e.g. Speed) 301 to an API host
  if ! meta=$(curl -fsSL -A 'lightning-address-connect-smoke/1.0' \
      "https://${domain}/.well-known/lnurlp/${user}"); then
    echo "meta: FAIL (could not fetch)"
    echo
    return 0
  fi
  local callback min inv_url inv
  callback=$(python3 -c "import sys,json; print(json.load(sys.stdin)['callback'])" <<<"$meta")
  min=$(python3 -c "import sys,json; print(json.load(sys.stdin).get('minSendable',1000))" <<<"$meta")
  if [[ "$callback" == *\?* ]]; then
    inv_url="${callback}&amount=${min}"
  else
    inv_url="${callback}?amount=${min}"
  fi
  if ! inv=$(curl -fsSL -A 'lightning-address-connect-smoke/1.0' "$inv_url"); then
    echo "invoice: FAIL (could not fetch callback)"
    echo
    return 0
  fi
  EXPECT_VERIFY="$expect_verify" python3 -c '
import json, os, sys
inv = json.load(sys.stdin)
has_verify = bool(inv.get("verify"))
expect = os.environ["EXPECT_VERIFY"] == "yes"
print("invoice:", "yes" if inv.get("pr") else "no")
print("lud21_verify:", "yes" if has_verify else "NO")
print("matches_expectation:", "yes" if has_verify == expect else "NO")
print("compatible_with_plugin:", "yes" if has_verify else "no")
' <<<"$inv"
  echo
}

# Supported (expect verify)
probe yes "hello@getalby.com"
probe yes "bitcoin@blink.sv"
probe yes "speed@speed.app"
probe yes "k00b@stacker.news"

# Unsupported today (expect no verify). Same crowdfund description-hash path would
# apply if they add LUD-21; until then setup fails on missing verify.
probe no "jack@strike.me"
probe no "test@walletofsatoshi.com"
probe no "coinos@coinos.io"
probe no "zbd@zbd.gg"

echo "Done."
