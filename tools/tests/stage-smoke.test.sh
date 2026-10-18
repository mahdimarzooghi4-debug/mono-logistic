#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
smoke_script="$repo_root/tools/stage-smoke.sh"
tmp_dir="$(mktemp -d)"
mock_bin="$tmp_dir/bin"
mock_log="$tmp_dir/curl.log"
mkdir -p "$mock_bin"
: > "$mock_log"
trap 'rm -rf "$tmp_dir"' EXIT

cat > "$mock_bin/curl" <<'MOCK_CURL'
#!/usr/bin/env bash
set -euo pipefail
url="${@: -1}"
printf '%s\n' "$url" >> "$MOCK_CURL_LOG"
if [[ "${MOCK_CURL_MODE:-success}" == "request-fail" ]]; then
  exit 7
fi
printf '%s' "${MOCK_CURL_STATUS:-200}"
MOCK_CURL
chmod +x "$mock_bin/curl"

invoke_smoke() {
  local mode="$1"
  local url="$2"
  local status="$3"
  PATH="$mock_bin:$PATH" \
    MOCK_CURL_LOG="$mock_log" \
    MOCK_CURL_MODE="$mode" \
    MOCK_CURL_STATUS="$status" \
    "$smoke_script" "$url"
}

fail() {
  echo "FAIL: $*" >&2
  exit 1
}

: > "$mock_log"
if output="$(invoke_smoke success "https://stage.example.test" 200 2>&1)"; then
  [[ "$output" == *"health/live (HTTP 200)"* ]] || fail "live endpoint success was not reported"
  [[ "$output" == *"health/ready (HTTP 200)"* ]] || fail "ready endpoint success was not reported"
else
  fail "HTTPS success case failed: $output"
fi
[[ "$(wc -l < "$mock_log")" -eq 2 ]] || fail "success case did not check both endpoints"

: > "$mock_log"
if output="$(invoke_smoke success "http://stage.example.test" 200 2>&1)"; then
  fail "remote HTTP URL was accepted"
fi
[[ "$output" == *"HTTPS is required"* ]] || fail "remote HTTP rejection was unclear"
[[ ! -s "$mock_log" ]] || fail "curl ran for a rejected remote HTTP URL"

: > "$mock_log"
if output="$(invoke_smoke success "http://127.0.0.1:5180" 200 2>&1)"; then
  [[ "$output" == *"health/ready (HTTP 200)"* ]] || fail "localhost exception did not check ready endpoint"
else
  fail "localhost HTTP case failed: $output"
fi
[[ "$(wc -l < "$mock_log")" -eq 2 ]] || fail "localhost case did not check both endpoints"

: > "$mock_log"
if output="$(invoke_smoke success "https://stage.example.test" 503 2>&1)"; then
  fail "non-200 endpoint response was accepted"
fi
[[ "$output" == *"HTTP 503"* ]] || fail "non-200 response was not reported"
[[ "$(wc -l < "$mock_log")" -eq 1 ]] || fail "smoke test did not stop after the first unhealthy endpoint"

: > "$mock_log"
if output="$(invoke_smoke request-fail "https://stage.example.test" 200 2>&1)"; then
  fail "curl transport failure was accepted"
fi
[[ "$output" == *"request failed"* ]] || fail "transport failure was not reported"
[[ "$(wc -l < "$mock_log")" -eq 1 ]] || fail "smoke test did not stop after transport failure"

echo "PASS: staging smoke URL and response contract cases"
