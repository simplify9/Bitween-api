-- Database statements per exchange, from pg_stat_statements.
--
-- Reset, right before the measured run (prints the time the window starts):
--   psql -v reset=1 -f stats.sql
-- Report, after it:
--   psql -v since="'2026-10-14 10:00:00+00'" -v subscription_id=42 -f stats.sql
--
-- "since" is the time the reset printed; "subscription_id" is the seeded subscription
-- (results/seed.json), so only the exchanges of this run are counted. pg_stat_statements has no
-- time window of its own: everything since the reset is counted, including Bitween's background
-- work (scheduler, heartbeats, metrics flush), which is shown apart.
--
-- Bitween's tables are in the "infolink" schema with snake_case names: xchange, xchange_result,
-- outbox_message, xchange_promoted_properties and so on.

\set ON_ERROR_STOP on
\pset footer off

\if :{?reset}

CREATE EXTENSION IF NOT EXISTS pg_stat_statements;
SELECT pg_stat_statements_reset();
\pset tuples_only on
SELECT 'since=' || to_char(clock_timestamp() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"');

\else

-- Taken first, so the queries of this report are not in what it reports.
CREATE TEMP TABLE snap AS
SELECT queryid,
       regexp_replace(query, '\s+', ' ', 'g') AS query,
       calls, total_exec_time, rows,
       -- Foreign key checks PostgreSQL runs inside an insert (seen because track=all): not
       -- statements Bitween sends, so not round trips.
       query ~* '^SELECT .* FOR KEY SHARE OF' AS fk_check,
       -- Transaction control, and the connection reset Npgsql sends ahead of the next command
       -- on a connection taken from the pool (in the same round trip).
       query ~* '^(DISCARD ALL|BEGIN|COMMIT|ROLLBACK|SAVEPOINT|RELEASE)' AS control,
       -- Work Bitween does on a timer whatever the traffic, and this harness's own reset.
       query !~* '^SELECT .* FOR KEY SHARE OF'
         AND query ~* '(qrtz_|job_executions|cluster_node|cluster_lease|queue_snapshot|exchange_stats|error_stats|adapter_stats|retry_group_usage|delayed_retry|pg_stat_statements|clock_timestamp|information_schema)'
         AS background
FROM pg_stat_statements
WHERE dbid = (SELECT oid FROM pg_database WHERE datname = current_database());

CREATE TEMP TABLE run AS
SELECT (SELECT count(*) FROM infolink.xchange x
        WHERE x.subscription_id = :subscription_id AND x.started_on >= :since::timestamptz) AS exchanges,
       (SELECT count(*) FROM infolink.xchange_result r JOIN infolink.xchange x ON x.id = r.id
        WHERE x.subscription_id = :subscription_id AND x.started_on >= :since::timestamptz) AS finished,
       (SELECT count(*) FROM infolink.xchange_result r JOIN infolink.xchange x ON x.id = r.id
        WHERE x.subscription_id = :subscription_id AND x.started_on >= :since::timestamptz AND r.success) AS succeeded,
       (SELECT count(*) FROM infolink.xchange
        WHERE started_on >= :since::timestamptz) AS all_exchanges,
       extract(epoch FROM clock_timestamp() - :since::timestamptz) AS seconds;

\echo
\echo '== Exchanges in the window'
SELECT exchanges, finished, succeeded,
       all_exchanges AS "all subscriptions",
       round(seconds::numeric, 1) AS "seconds since reset"
FROM run;

\echo
\echo '== Statements per exchange (pg_stat_statements calls / exchanges of this run)'
SELECT sum(calls) AS "statements",
       sum(calls) FILTER (WHERE NOT background AND NOT fk_check) AS "exchange path",
       sum(calls) FILTER (WHERE fk_check) AS "fk checks",
       sum(calls) FILTER (WHERE background) AS "background",
       round(sum(calls)::numeric / nullif((SELECT exchanges FROM run), 0), 2) AS "all per exchange",
       round((sum(calls) FILTER (WHERE NOT background AND NOT fk_check))::numeric
             / nullif((SELECT exchanges FROM run), 0), 2) AS "path per exchange",
       round((sum(calls) FILTER (WHERE NOT background AND NOT fk_check AND NOT control))::numeric
             / nullif((SELECT exchanges FROM run), 0), 2) AS "sql per exchange",
       round((sum(total_exec_time) FILTER (WHERE NOT background AND NOT fk_check))::numeric
             / nullif((SELECT exchanges FROM run), 0), 3) AS "path ms per exchange"
FROM snap;

\echo
\echo '== Top statements by calls (exchange path)'
SELECT calls,
       round(calls::numeric / nullif((SELECT exchanges FROM run), 0), 2) AS "per exch",
       round(total_exec_time::numeric, 1) AS "total ms",
       round((total_exec_time / nullif(calls, 0))::numeric, 3) AS "mean ms",
       rows,
       left(query, 150) AS query
FROM snap WHERE NOT background AND NOT fk_check
ORDER BY calls DESC LIMIT 25;

\echo
\echo '== Top statements by total time (exchange path)'
SELECT round(total_exec_time::numeric, 1) AS "total ms",
       calls,
       round((total_exec_time / nullif(calls, 0))::numeric, 3) AS "mean ms",
       left(query, 150) AS query
FROM snap WHERE NOT background AND NOT fk_check
ORDER BY total_exec_time DESC LIMIT 15;

\echo
\echo '== Background and foreign key check statements (not counted in "path per exchange")'
SELECT CASE WHEN fk_check THEN 'fk check' ELSE 'background' END AS kind,
       calls, round(total_exec_time::numeric, 1) AS "total ms", left(query, 150) AS query
FROM snap WHERE background OR fk_check
ORDER BY calls DESC LIMIT 15;

\endif
