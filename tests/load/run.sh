#!/usr/bin/env bash
# Sync gateway load test, end to end: stack up, seed, reset pg_stat_statements, k6 at a fixed
# rate, then the latency summary and the database statements per exchange.
#
#   tests/load/run.sh                         20/s for 2m after a 15s warm-up
#   RATE=10 DURATION=30s tests/load/run.sh    a short run
#   DOWN=1 tests/load/run.sh                  stop the stack and delete its data at the end
#
# Other settings (see sync.k6.js): WARMUP, MAX_VUS, P95_BUDGET_MS, P99_BUDGET_MS, ERROR_BUDGET,
# WAIT_PERIOD. PRIME: sequential calls before the measured window (default 30).
# NO_BUILD=1 skips rebuilding the Bitween image. PARTNER_DELAY_MS: the partner answers after
# that many milliseconds (the slow-partner service) instead of at once.
# Exit code: k6's (non-zero when a threshold failed), or 1 when the harness itself failed.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
cd "$here"

# Colima keeps its socket in the home folder; Docker Desktop needs nothing set.
if [[ -z "${DOCKER_HOST:-}" && -S "$HOME/.colima/default/docker.sock" ]]; then
  export DOCKER_HOST="unix://$HOME/.colima/default/docker.sock"
fi

RATE="${RATE:-20}"
DURATION="${DURATION:-2m}"
WARMUP="${WARMUP:-15s}"

# A partner that takes a known time, for measuring a sync call against it.
if [[ -n "${PARTNER_DELAY_MS:-}" ]]; then
  export PARTNER_URL="http://slow-partner:8080/orders"
fi

log() { printf '\n\033[1m== %s\033[0m\n' "$*"; }
die() { printf 'run.sh: %s\n' "$*" >&2; exit 1; }
compose() { docker compose "$@"; }
psql_db() { compose exec -T postgres psql -X -q -U bitween -d bitween "$@"; }
# S3 requests SeaweedFS has served on Bitween's bucket so far, by type ("GET 30 PUT 12 ..."), from
# the count of its request-time histogram. It serves metrics on the container's address only.
s3_requests() {
  compose exec -T s3 wget -qO- http://s3:9324/metrics 2>/dev/null | python3 -c '
import re, sys
counts = {}
for line in sys.stdin:
    m = re.match(r"SeaweedFS_s3_request_seconds_count\{([^}]*)\} ([0-9.e+]+)", line)
    if not m or "bitween-load" not in m.group(1): continue
    kind = re.search(r"type=\"([^\"]*)\"", m.group(1))
    key = kind.group(1) if kind else "?"
    counts[key] = counts.get(key, 0) + float(m.group(2))
print(" ".join(f"{k} {int(v)}" for k, v in sorted(counts.items())))'
}

# The repository has no NuGet.config; a developer may keep an untracked one that names local
# package folders, and the image build cannot see those. Build from a copy of the Dockerfile
# whose own ignore file leaves NuGet.config out. Without one, the repository's Dockerfile is used.
if [[ -f "$repo/NuGet.config" || -f "$repo/nuget.config" ]]; then
  mkdir -p "$here/.build"
  cp "$repo/Dockerfile" "$here/.build/Dockerfile"
  { cat "$repo/.dockerignore"; printf '\nNuGet.config\nnuget.config\ntests\n'; } > "$here/.build/Dockerfile.dockerignore"
  export BITWEEN_DOCKERFILE="tests/load/.build/Dockerfile"
fi

mkdir -p "$here/results"
# k6 runs as an unprivileged user in its container and writes its summaries here.
chmod 777 "$here/results"
rm -f "$here/results/seed.json" "$here/results/summary.json"

log "Starting the stack"
build=(--build)
[[ "${NO_BUILD:-}" == 1 ]] && build=()
compose up -d "${build[@]}" --wait --wait-timeout 600 || {
  compose logs --tail 60 bitween >&2 || true
  die "the stack did not become healthy"
}

log "Waiting for Bitween /health/ready"
for i in $(seq 1 120); do
  compose exec -T bitween python3 -c "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8080/health/ready', timeout=4)" \
    >/dev/null 2>&1 && break
  [[ $i -eq 120 ]] && { compose logs --tail 60 bitween >&2; die "Bitween did not answer /health/ready"; }
  sleep 1
done

log "Seeding (information type, partner and key, subscription, gateway), then priming"
compose run --rm -e RUN_ID="${RUN_ID:-}" -e PRIME="${PRIME:-30}" k6 run --quiet --no-usage-report seed.k6.js
[[ -s "$here/results/seed.json" ]] || die "seeding wrote no results/seed.json"
subscription_id="$(python3 -c "import json,sys; print(json.load(open(sys.argv[1]))['subscriptionId'])" "$here/results/seed.json")"

has_db_commands="$(psql_db -tA -c "select count(*) from information_schema.columns
  where table_schema = 'infolink' and table_name = 'exchange_stats' and column_name = 'db_commands'")"

log "Resetting pg_stat_statements"
since="$(psql_db -tA -v reset=1 -f - < "$here/stats.sql" | sed -n 's/^since=//p' | tail -1)"
[[ -n "$since" ]] || die "could not reset pg_stat_statements"
echo "window starts $since"

s3_before="$(s3_requests || true)"

log "k6: ${RATE}/s for ${DURATION} after a ${WARMUP} warm-up${PARTNER_DELAY_MS:+, partner answering in ${PARTNER_DELAY_MS} ms}"
set +e
compose run --rm \
  -e RATE="$RATE" -e DURATION="$DURATION" -e WARMUP="$WARMUP" \
  -e MAX_VUS="${MAX_VUS:-200}" -e WAIT_PERIOD="${WAIT_PERIOD:-30}" \
  -e P95_BUDGET_MS="${P95_BUDGET_MS:-3000}" -e P99_BUDGET_MS="${P99_BUDGET_MS:-5000}" \
  -e ERROR_BUDGET="${ERROR_BUDGET:-0.01}" \
  k6 run --no-usage-report sync.k6.js
k6_exit=$?
set -e

# Let the last exchanges finish before the statements are read.
sleep 3

log "Database statements (pg_stat_statements)"
psql_db -v since="'$since'" -v subscription_id="$subscription_id" -f - < "$here/stats.sql"

log "Object storage requests (SeaweedFS S3 metrics)"
s3_after="$(s3_requests || true)"
window_exchanges="$(psql_db -tA -c "select count(*) from infolink.xchange
  where subscription_id = $subscription_id and started_on >= '$since'::timestamptz")"
python3 - "$s3_before" "$s3_after" "$window_exchanges" <<'PY'
import sys
def parse(text):
    parts = text.split()
    return {parts[i]: int(parts[i + 1]) for i in range(0, len(parts) - 1, 2)}
before, after, exchanges = parse(sys.argv[1]), parse(sys.argv[2]), int(sys.argv[3] or 0)
if not after:
    print("not available: no SeaweedFS_s3_request_seconds_count in the s3 metrics")
    sys.exit(0)
total = 0
for kind in sorted(after):
    delta = after[kind] - before.get(kind, 0)
    total += delta
    print(f"{kind:>8}: {delta:>7}  {delta / exchanges if exchanges else float('nan'):6.2f} per exchange")
print(f"{'all':>8}: {total:>7}  {total / exchanges if exchanges else float('nan'):6.2f} per exchange ({exchanges} exchanges)")
PY

log "Bitween's own count (infolink.exchange_stats.db_commands)"
if [[ "$has_db_commands" == 1 ]]; then
  # Counted in memory and added to the hourly rows once a minute: wait for the run's exchanges.
  expected="$(psql_db -tA -c "select count(*) from infolink.xchange where subscription_id = $subscription_id")"
  for i in $(seq 1 90); do
    flushed="$(psql_db -tA -c "select coalesce(sum(total), 0) from infolink.exchange_stats where subscription_id = $subscription_id")"
    [[ "$flushed" -ge "$expected" ]] && break
    sleep 1
  done
  psql_db -c "select sum(total) as exchanges, sum(db_commands) as db_commands,
      round(sum(db_commands)::numeric / nullif(sum(total), 0), 2) as \"db commands per exchange\"
    from infolink.exchange_stats where subscription_id = $subscription_id"
else
  echo "not available: infolink.exchange_stats has no db_commands column in this build"
fi

log "Results"
echo "k6 summary:   $here/results/summary.json (full: results/k6-full.json)"
if [[ "${DOWN:-}" == 1 ]]; then
  compose down -v
else
  echo "stack left running; stop it with: (cd $here && docker compose down -v)"
fi
exit "$k6_exit"
