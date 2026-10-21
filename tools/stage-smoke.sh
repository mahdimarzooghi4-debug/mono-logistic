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

home_url="${base_url}/"
home_body="$(curl --silent --show-error --fail --connect-timeout 5 --max-time 15 "$home_url")" || {
  echo "FAIL $home_url (request failed)" >&2
  exit 1
}
asset_path="$(sed -nE 's/.*src="([^"]+\\.js)".*/\\1/p' <<<"$home_body" | head -n1)"
if [[ "$home_body" != *'id="root"'* || "$asset_path" != /assets/*.js ]]; then
  echo "FAIL $home_url (Operations Console shell or built asset reference missing)" >&2
  exit 1
fi
asset_url="${base_url}${asset_path}"
asset_status="$(curl --silent --show-error --output /dev/null --write-out '%{http_code}' \
  --connect-timeout 5 --max-time 15 "$asset_url")" || {
    echo "FAIL $asset_url (request failed)" >&2
    exit 1
  }
if [[ "$asset_status" != "200" ]]; then
  echo "FAIL $asset_url (HTTP $asset_status)" >&2
  exit 1
fi
echo "PASS $home_url (Operations Console shell and JavaScript asset served)"

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
