# Load test: synchronous gateway calls

The harness from `docs/design/processing-performance.md` (finding 4, phase 0, and finding 10). It
measures one synchronous API gateway call (`POST /api/gateway/{urlName}/sync`) at a fixed
throughput: end-to-end latency, exchanges per second, and database statements per exchange.

## Run it

Docker with Compose v2 is all it needs; k6 runs in a container on the stack's network.

```bash
tests/load/run.sh                              # 20/s for 2 minutes, after a 15 s warm-up
RATE=10 DURATION=30s WARMUP=5s tests/load/run.sh
DOWN=1 tests/load/run.sh                       # stop the stack and delete its data at the end
```

On Colima, `run.sh` sets `DOCKER_HOST` to `~/.colima/default/docker.sock` when it is not set.
The first run builds the Bitween image from the repository's `Dockerfile` (several minutes);
`NO_BUILD=1` reuses the last image. The stack stays up after a run (Bitween on
`http://localhost:8090`, admin `admin@Bitween.systems` / `LoadTest-Admin-2026!`) until
`docker compose down -v` in this folder.

| Setting | Default | Meaning |
|---|---|---|
| `RATE` | 20 | calls started per second (constant arrival rate, whatever the latency) |
| `DURATION` | 2m | length of the measured phase |
| `WARMUP` | 15s | same rate before it, not in the latency figures (`0s` for none) |
| `P95_BUDGET_MS`, `P99_BUDGET_MS` | 3000, 5000 | k6 thresholds; a breach makes the run exit non-zero |
| `ERROR_BUDGET` | 0.01 | share of calls allowed to answer anything but 200 |
| `MAX_VUS` | 200 | most calls in flight at once; above it k6 drops iterations and says so |
| `WAIT_PERIOD` | 30 | seconds Bitween may hold a sync call for its result (`Wait-Period` header) |
| `PRIME` | 30 | sync calls made one at a time after seeding, before the measured window |

## What runs

| File | Role |
|---|---|
| `docker-compose.yml` | PostgreSQL 16 with `pg_stat_statements`, RabbitMQ 3, SeaweedFS as S3 storage, an nginx "partner" answering `{"accepted":true}` at once, Bitween built from `../../Dockerfile`, and k6 (profile `k6`) |
| `seed.k6.js`, `lib/seed.js` | Through the admin API: signs in as the seeded administrator (and does the forced password change), creates a JSON information type, a partner with a key, a `GatewayApiCall` subscription whose handler is the native HTTP handler posting to the partner (no mapper, no notifications), and an API gateway with the partner attached by key. Names carry a run id, so every run seeds a fresh set. Writes `results/seed.json`, then primes the gateway with `PRIME` sequential calls. |
| `sync.k6.js` | The load. Reads `results/seed.json`, or seeds in `setup()` when it is missing, so it also runs on its own: `docker compose run --rm k6 run sync.k6.js` |
| `stats.sql` | Resets `pg_stat_statements`, and reports statements per exchange and the top statements |
| `run.sh` | All of the above in order, then Bitween's own command count |

Seeding and priming run before the reset, so their statements are not counted. Priming matters:
on a node fresh from start, a first trial at 10/s with only a 5 s warm-up collapsed (p95 42 s,
37% of calls failed, Npgsql timing out waiting for a pooled connection), while the same rate on a
primed node answered every call. That cold-start behaviour is worth a look of its own; the
harness measures the warm node.

## The numbers

- **p50, p95, p99**: time from the partner's request to Bitween's answer, measured by k6, for the
  measured phase only. The sync call includes the wait for the exchange to be processed, so this
  is the full path: create, queue, process, handler, result, answer.
- **exchanges/s**: calls answered 200 divided by the measured duration. At a fixed arrival rate
  this equals `RATE` while Bitween keeps up; below it (or dropped iterations) means it did not.
- **statements per exchange**: `pg_stat_statements` calls since the reset divided by the exchanges
  the seeded subscription created in that window (warm-up included in both).
  - "all per exchange": every statement.
  - "path per exchange": leaves out statements Bitween runs on a timer whatever the traffic
    (scheduler `qrtz_` tables, cluster heartbeat, metrics flush, queue snapshots, delayed retry
    poll) and the foreign key checks PostgreSQL runs inside an insert (visible because of
    `track=all`; not sent by Bitween). Both are listed separately.
  - "sql per exchange": the path without `BEGIN`, `COMMIT` and `DISCARD ALL`. `DISCARD ALL` is
    the reset Npgsql sends ahead of the first command on a connection taken back from the pool,
    in the same round trip, so it costs a statement but not a trip. This is the column to watch
    for statements removed by a phase.
- **db commands per exchange**: Bitween's own figure, `sum(db_commands) / sum(total)` from
  `infolink.exchange_stats` for the seeded subscription, counted by the phase 0 interceptor.
  Shown only when the build has that column. It counts commands (round trips): one
  `SaveChanges` batch is one command but several statements, so it reads lower than the
  `pg_stat_statements` figure. Both are kept; the targets below are in round trips.

## Acceptance per phase (finding 4)

Each phase is accepted on this harness at the same `RATE` and `DURATION` as the baseline.

| Phase | Steps | DB commands per sync exchange | p95 | exchanges/s |
|---|---|---|---|---|
| 0 | baseline: counter, stage timings, this harness | about 14 (recorded) | recorded | recorded |
| 1 | 4e, 4g, 4d, 4h, 4f: reads folded or moved to the cache | about 9 | no regression | no regression |
| 2 | 4a, 4b: outbox insert and delete in batch, partial index | about 7 | no regression | no regression |
| 3 | 4c, 4i: result event only when something notifies, auto-pause on results | about 5 | no regression | no regression |
| 4 | 4j, 4k: promoted properties at creation, optimistic idempotency insert | about 4 | no regression | no regression |

`pg_stat_statements` should also show each removed statement gone from the top list.

## Notes

- The storage is SeaweedFS because MinIO no longer publishes images on Docker Hub. It is
  S3-compatible and Bitween talks to it with its S3 provider, as production does. Bitween creates
  the bucket at startup.
- RabbitMQ's health check is a plain TCP check: `rabbitmq-diagnostics` runs as root and, when it
  runs before the broker has started, writes an Erlang cookie the broker then cannot read.
- Rate limits are raised in the compose file: production allows 600 requests a minute per client
  address, and k6 is one address.
- The registry is switched off (`Bitween__RegistryEnabled=false`) so the stack makes no calls out.
- When the checkout has a `NuGet.config` (the repository has none; a developer may keep one that
  names local package folders), `run.sh` builds from a copy of the `Dockerfile` in `.build/` with
  an ignore file that leaves it out, since those folders do not exist inside the build. A plain
  `docker compose up -d --build` uses the repository's `Dockerfile` as it is.
- Results land in `results/` (ignored by git): `summary.json` with the headline numbers, and
  `k6-full.json` with every metric.
