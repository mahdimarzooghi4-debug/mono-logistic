#!/usr/bin/env bash
set -euo pipefail

base_url="${1:-${MONO_STAGE_BASE_URL:-}}"
if [[ -z "$base_url" ]]; then
  echo "Usage: MONO_STAGE_BASE_URL=https://<stage-host> $0 [base-url]" >&2
  exit 2
fi

base_url="${base_url%/}"
if [[ ! "$base_url" =~ ^https?://([^/:?#]+)(:[0-9]+)?(/[^?#]*)?$ ]]; then
  echo "Invalid base URL: expected an http(s) URL without query or fragment." >&2
  exit 2
fi

host="${BASH_REMATCH[1],,}"
if [[ "$base_url" != https://* && "$host" != "localhost" && "$host" != "127.0.0.1" && "$host" != "::1" ]]; then
  echo "HTTPS is required except for localhost smoke tests." >&2
  exit 2
fi

for endpoint in health/live health/ready; do
  url="${base_url}/${endpoint}"
  status="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
    --connect-timeout 5 --max-time 15 "$url")" || {
      echo "FAIL $url (request failed)" >&2
      exit 1
    }
  if [[ "$status" != "200" ]]; then
    echo "FAIL $url (HTTP $status)" >&2
    exit 1
  fi
  echo "PASS $url (HTTP 200)"
done
