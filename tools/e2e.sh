#!/usr/bin/env bash
#
# Runs the Playwright end-to-end suite against a throwaway Bitween: a fresh PostgreSQL 16 and
# RabbitMQ in Docker, the UI and backend built from this checkout, and the app started on
# https://localhost:7155 with its own local storage bucket. Everything is torn down afterwards.
#
#   tools/e2e.sh                     fresh environment, run the suite, tear down
#   tools/e2e.sh --keep              ...and leave the environment running afterwards
#   tools/e2e.sh --reuse             run against an environment a --keep run left behind
#   tools/e2e.sh --up                start a fresh environment and leave it running, no tests
#   tools/e2e.sh --down              tear down a kept environment
#   tools/e2e.sh --no-build          skip building the UI and the backend
#   tools/e2e.sh -- <args>           anything after -- goes to `playwright test`,
#                                    e.g. tools/e2e.sh -- e2e/exchanges.spec.ts
#
# The suite needs nothing from the database it runs against: its `seed` project creates what it
# uses. That is the point of this script — a run proves the suite works from nothing, rather than
# from whatever the last person's dev database happened to hold.
#
# Environment:
#   E2E_ADMIN_PASSWORD   the seeded administrator's password. A fresh install refuses to start
#                        without one (Bitween__InitialAdminPassword), so a random one is generated
#                        when unset and handed to both the app and the suite.
#   DOCKER_HOST          respected as usual (Colima: unix://$HOME/.colima/default/docker.sock).

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WEB="$ROOT/SW.Bitween.Web"
CLIENT="$WEB/ClientApp"

PG_CONTAINER=bitween-e2e-postgres
MQ_CONTAINER=bitween-e2e-rabbitmq
PG_PORT="${E2E_PG_PORT:-55432}"
MQ_PORT="${E2E_MQ_PORT:-55672}"
MQ_MGMT_PORT="${E2E_MQ_MGMT_PORT:-55673}"
APP_PORT=7155
APP_URL="https://localhost:$APP_PORT"

# Its own bucket, not the "bitween" one a developer's local instance uses: a fresh run wipes it,
# and the default bucket is where that instance keeps its installed adapters.
BUCKET=bitween-e2e
BUCKET_DIR="${TMPDIR:-/tmp}"
BUCKET_DIR="${BUCKET_DIR%/}/SW.CloudFiles.LocalTests/$BUCKET"

# Where a kept environment remembers its app process and password between invocations.
STATE="${TMPDIR:-/tmp}"
STATE="${STATE%/}/bitween-e2e"
PID_FILE="$STATE/app.pid"
PASSWORD_FILE="$STATE/admin-password"
APP_LOG="$STATE/app.log"

keep=false reuse=false up_only=false down_only=false build=true
playwright_args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --keep) keep=true ;;
    --reuse) reuse=true; keep=true ;;
    --up) up_only=true; keep=true ;;
    --down) down_only=true ;;
    --no-build) build=false ;;
    --) shift; playwright_args=("$@"); break ;;
    -h|--help) sed -n '2,25p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "unknown option: $1 (see --help)" >&2; exit 2 ;;
  esac
  shift
done

log() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
die() { printf '\n\033[31me2e: %s\033[0m\n' "$*" >&2; exit 1; }

stop_app() {
  [[ -f "$PID_FILE" ]] || return 0
  local pid
  pid="$(cat "$PID_FILE")"
  # `dotnet run` starts the app as a child process, so stopping only the parent would leave the
  # app holding the port.
  pkill -TERM -P "$pid" 2>/dev/null || true
  kill -TERM "$pid" 2>/dev/null || true
  for _ in $(seq 1 20); do kill -0 "$pid" 2>/dev/null || break; sleep 0.5; done
  pkill -KILL -P "$pid" 2>/dev/null || true
  kill -KILL "$pid" 2>/dev/null || true
  rm -f "$PID_FILE"
}

teardown() {
  stop_app
  docker rm -f "$PG_CONTAINER" "$MQ_CONTAINER" >/dev/null 2>&1 || true
  rm -rf "$BUCKET_DIR"
  rm -f "$PASSWORD_FILE"
}

if $down_only; then
  log "Tearing down the e2e environment"
  teardown
  exit 0
fi

command -v docker >/dev/null || die "docker is not installed"
docker info >/dev/null 2>&1 || die "can't reach the Docker daemon (DOCKER_HOST=${DOCKER_HOST:-unset})"
mkdir -p "$STATE"

# These ports sit inside Linux's ephemeral range, so inside the Docker VM an outgoing connection
# (Testcontainers traffic from another test run, say) can briefly hold one as its local port and
# the bind fails with "address already in use" although nothing is listening. That clears in
# seconds, so a few retries beat a run that dies for no reason anyone can see.
start_container() {
  local out
  for i in $(seq 1 10); do
    out="$(docker run -d --rm "$@" 2>&1)" && return 0
    [[ "$out" == *"address already in use"* && $i -lt 10 ]] || die "docker run failed: $out"
    sleep 3
  done
}

app_up() { curl -skf -o /dev/null "$APP_URL/health"; }

# Adapters are published by writing a package into the storage bucket, which with local storage is
# a directory: the object at its key and its metadata beside it as <key>.meta.json — the layout
# AdapterInstaller reads back (see SW.Bitween.IntegrationTests/Fixtures/AdapterInstaller.cs, which
# publishes the same way through ICloudFilesService).
ADAPTERS="$STATE/adapters"
SAMPLE_HANDLER=SW.Bitween.SampleHandler
PG_ADAPTER=SW.Bitween.Adapters.Db.PostgreSql

# publish_adapter <project> <adapter id> <entry assembly> [version] [extra metadata as '"k":"v",']
publish_adapter() {
  local project="$1" id="$2" entry="$3" version="${4:-}" extra="${5:-}"
  local key="adapters/$id"
  [[ -n "$version" ]] && key="adapters-versions/$id/$version"
  local target="$BUCKET_DIR/$key" zip="$STATE/$id.zip"
  rm -f "$zip"
  (cd "$ADAPTERS/$project" && zip -qrX "$zip" . -x '*.pdb' '*.xml' '*.http')
  local sha
  sha="$(shasum -a 256 "$zip" | cut -d' ' -f1)"
  mkdir -p "$(dirname "$target")"
  mv "$zip" "$target"
  printf '{%s"EntryAssembly":"%s","Hash":"%s","Sha256":"%s"}' "$extra" "$entry" "${sha:0:16}" "$sha" \
    >"$target.meta.json"
}

# The catalog entry an installer writes beside a versioned adapter (adapters-catalog/<id>.json, in
# SW.Serverless.Contract's AdapterCatalogEntry shape): which version is current and what each one
# is. Versions are offered for pinning from here, not from the package files.
# publish_catalog <adapter id> <entry assembly> <display name> <current> <version>...
publish_catalog() {
  local id="$1" entry="$2" name="$3" current="$4"
  shift 4
  local manifest versions="" v
  manifest() {
    printf '{"id":"%s","version":"%s","displayName":"%s","summary":"Hands back the document it is given.",' "$id" "$1" "$name"
    printf '"publisher":{"name":"Bitween e2e"},"kinds":["handler"],"entry":"%s","releaseNotes":"Release %s.",' "$entry" "$1"
    printf '"properties":[{"name":"ContentType","description":"The content type it answers with.","default":"text/plain"}]}'
  }
  for v in "$@"; do
    versions+="${versions:+,}{\"version\":\"$v\",\"publishedOn\":\"2026-01-01T00:00:00Z\",\"publishedBy\":\"tools/e2e.sh\",\"manifest\":$(manifest "$v")}"
  done
  mkdir -p "$BUCKET_DIR/adapters-catalog"
  printf '{"catalogVersion":1,"id":"%s","current":"%s","manifest":%s,"versions":[%s]}' \
    "$id" "$current" "$(manifest "$current")" "$versions" >"$BUCKET_DIR/adapters-catalog/$id.json"
  printf '{}' >"$BUCKET_DIR/adapters-catalog/$id.json.meta.json"
}

if $reuse; then
  app_up || die "no e2e environment is running at $APP_URL — start one with tools/e2e.sh --keep or --up"
  [[ -f "$PASSWORD_FILE" ]] && E2E_ADMIN_PASSWORD="${E2E_ADMIN_PASSWORD:-$(cat "$PASSWORD_FILE")}"
else
  # Fresh means fresh: whatever an earlier --keep left behind goes first.
  teardown
  if lsof -nP -iTCP:"$APP_PORT" -sTCP:LISTEN >/dev/null 2>&1; then
    die "something else is already listening on port $APP_PORT — stop it first (a local Bitween?)"
  fi

  if [[ -z "${E2E_ADMIN_PASSWORD:-}" ]]; then
    # Meets the password policy whatever the random part turns out to be: upper, lower, digit
    # and a symbol are all present in the fixed prefix.
    E2E_ADMIN_PASSWORD="E2e!pw-$(openssl rand -hex 12)"
  fi
  [[ "$E2E_ADMIN_PASSWORD" != 'Mtm@dmin!2' ]] ||
    die "E2E_ADMIN_PASSWORD can't be the published default password: a fresh install refuses it"
  umask 077
  printf '%s' "$E2E_ADMIN_PASSWORD" >"$PASSWORD_FILE"
  umask 022
fi
export E2E_ADMIN_PASSWORD
export E2E_BASE_URL="$APP_URL/"

if ! $keep; then
  trap 'log "Tearing down"; teardown' EXIT
elif ! $reuse; then
  # Kept, but a failure on the way up should still not leave half an environment behind.
  trap 'status=$?; if [[ $status -ne 0 && "${started:-false}" != true ]]; then teardown; fi' EXIT
fi

if ! $reuse; then
  log "Starting PostgreSQL 16 on $PG_PORT and RabbitMQ on $MQ_PORT (management $MQ_MGMT_PORT)"
  start_container -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=bitween \
    --name "$PG_CONTAINER" -p "$PG_PORT:5432" postgres:16
  start_container --name "$MQ_CONTAINER" \
    -p "$MQ_PORT:5672" -p "$MQ_MGMT_PORT:15672" rabbitmq:3.13-management

  # The builds don't need either container, so they run while the containers start.
  if $build; then
    log "Building the UI"
    (cd "$CLIENT" && { [[ -d node_modules ]] || yarn install --frozen-lockfile; } && yarn build)
    log "Building the backend"
    (cd "$WEB" && dotnet build --nologo -v q)
  fi
  if $build || [[ ! -d "$ADAPTERS/$SAMPLE_HANDLER" || ! -d "$ADAPTERS/$PG_ADAPTER" ]]; then
    log "Building the adapters the suite publishes"
    for project in "$SAMPLE_HANDLER" "$PG_ADAPTER"; do
      dotnet publish "$ROOT/$project" --nologo -v q -clp:ErrorsOnly -c Debug --no-self-contained \
        -o "$ADAPTERS/$project"
    done
  fi

  log "Waiting for PostgreSQL"
  # Over TCP on purpose: the image's init phase runs a server on the unix socket only, then
  # restarts it, so a socket check can pass before the real server is up.
  for i in $(seq 1 60); do
    docker exec "$PG_CONTAINER" pg_isready -q -h 127.0.0.1 -U postgres -d bitween && break
    [[ $i -eq 60 ]] && die "PostgreSQL did not become ready"
    sleep 1
  done

  log "Waiting for RabbitMQ"
  # The app fails at startup if the broker isn't accepting connections, and the management API
  # comes up last — waiting on it covers both.
  for i in $(seq 1 90); do
    curl -sf -u guest:guest -o /dev/null "http://localhost:$MQ_MGMT_PORT/api/overview" &&
      docker exec "$MQ_CONTAINER" rabbitmq-diagnostics -q check_port_listener 5672 >/dev/null 2>&1 && break
    [[ $i -eq 90 ]] && die "RabbitMQ did not become ready"
    sleep 1
  done

  rm -rf "$BUCKET_DIR"
  log "Publishing adapters to the e2e bucket"
  # A custom handler with two published versions, for the version picker and the Custom section
  # of the Adapters page, and the PostgreSQL provider, for data sources — run against the e2e
  # database itself. Bitween's own (bitween.) prefix is what makes the latter a provider.
  for version in "" 1.0.0 2.0.0; do
    publish_adapter "$SAMPLE_HANDLER" e2e.samplehandler SW.Bitween.SampleHandler.dll "$version" '"Kind":"handler",'
  done
  publish_catalog e2e.samplehandler SW.Bitween.SampleHandler.dll "Echo handler (e2e)" 2.0.0 1.0.0 2.0.0
  publish_adapter "$PG_ADAPTER" bitween.db.postgresql SW.Bitween.Adapters.Db.PostgreSql.dll "" \
    '"Protocol":"2","Lifecycle":"resident",'

  log "Starting Bitween on $APP_URL (log: $APP_LOG)"
  (
    cd "$WEB"
    export ASPNETCORE_ENVIRONMENT=Development
    export ASPNETCORE_URLS="$APP_URL"
    export Bitween__DatabaseType=PgSql
    export ConnectionStrings__BitweenDb="Host=localhost;Port=$PG_PORT;Database=bitween;Username=postgres;Password=postgres"
    export ConnectionStrings__RabbitMQ="amqp://guest:guest@localhost:$MQ_PORT/"
    export Bitween__StorageProvider=Local
    export CloudFiles__BucketName="$BUCKET"
    export Token__Key="e2e-test-key-0123456789abcdefghijklmnopq" Token__Issuer=e2e Token__Audience=e2e
    export Bitween__RabbitMqManagementUrl="http://localhost:$MQ_MGMT_PORT"
    export Bitween__RabbitMqManagementUsername=guest Bitween__RabbitMqManagementPassword=guest
    # The suite signs in dozens of times a minute, which the production limits are there to stop.
    export Bitween__RateLimits__SignInPerMinute=100000 Bitween__RateLimits__RequestsPerMinute=1000000
    export Bitween__InitialAdminPassword="$E2E_ADMIN_PASSWORD"
    exec nohup dotnet run --no-build --no-launch-profile >"$APP_LOG" 2>&1
  ) &
  echo $! >"$PID_FILE"

  for i in $(seq 1 120); do
    app_up && break
    kill -0 "$(cat "$PID_FILE")" 2>/dev/null || { tail -40 "$APP_LOG" >&2; die "Bitween exited during startup"; }
    [[ $i -eq 120 ]] && { tail -40 "$APP_LOG" >&2; die "Bitween did not answer on $APP_URL/health"; }
    sleep 1
  done
  started=true
fi

if $up_only; then
  log "Environment is up at $APP_URL"
  echo "  admin:    admin@Bitween.systems / $E2E_ADMIN_PASSWORD"
  echo "  run:      tools/e2e.sh --reuse [-- <playwright args>]"
  echo "  stop:     tools/e2e.sh --down"
  exit 0
fi

cd "$CLIENT"
# Only the one browser the suite uses, and only when this machine doesn't have it yet.
npx playwright install chromium >/dev/null

log "Running the Playwright suite"
set +e
npx playwright test "${playwright_args[@]+"${playwright_args[@]}"}"
result=$?
set -e

if $keep; then
  log "Environment left running at $APP_URL"
  echo "  admin:    admin@Bitween.systems / $E2E_ADMIN_PASSWORD"
  echo "  rerun:    tools/e2e.sh --reuse"
  echo "  stop:     tools/e2e.sh --down"
fi
exit $result
