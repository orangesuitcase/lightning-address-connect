#!/usr/bin/env bash
# Smoke-test LNURL-pay + LUD-21 for sample addresses (no BTCPay required).
set -euo pipefail

probe() {
  local addr="$1"
  local user="${addr%@*}"
  local domain="${addr#*@}"
  echo "=== $addr ==="
  local meta
  meta=$(curl -fsS "https://${domain}/.well-known/lnurlp/${user}")
  local callback
  callback=$(python3 -c "import sys,json; print(json.load(sys.stdin)['callback'])" <<<"$meta")
  local min
  min=$(python3 -c "import sys,json; print(json.load(sys.stdin).get('minSendable',1000))" <<<"$meta")
  local inv
  inv=$(curl -fsS "${callback}?amount=${min}")
  python3 - <<PY
import json
inv=json.loads('''$inv''')
has_verify=bool(inv.get('verify'))
print('invoice:', 'yes' if inv.get('pr') else 'no')
print('lud21_verify:', 'yes' if has_verify else 'NO')
print('compatible:', 'yes' if has_verify else 'no')
PY
  echo
}

probe "hello@getalby.com"
# WoS: expect no verify
set +e
probe "test@walletofsatoshi.com"
set -e
echo "Done."
