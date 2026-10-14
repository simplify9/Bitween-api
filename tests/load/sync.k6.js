// Synchronous API gateway calls at a fixed arrival rate: POST /api/gateway/{urlName}/sync with a
// partner key, measured end to end.
//
// Uses results/seed.json when seed.k6.js wrote one (what run.sh does); otherwise seeds in setup(),
// so this file alone also works:  docker compose run --rm k6 run sync.k6.js
//
// Environment:
//   RATE            calls per second during the measured phase (default 20)
//   DURATION        length of the measured phase (default 2m)
//   WARMUP          warm-up at the same rate before it, not measured (default 15s, 0s for none)
//   MAX_VUS         ceiling on concurrent calls (default 200)
//   P95_BUDGET_MS   threshold on the measured p95 (default 3000)
//   P99_BUDGET_MS   threshold on the measured p99 (default 5000)
//   ERROR_BUDGET    threshold on the share of failed calls (default 0.01)
//   WAIT_PERIOD     seconds Bitween may hold a sync call for the result (default 30)
import http from 'k6/http';
import { check } from 'k6';
import exec from 'k6/execution';
import { Trend, Rate, Counter } from 'k6/metrics';
import { seed } from './lib/seed.js';

const rate = Number(__ENV.RATE || 20);
const duration = __ENV.DURATION || '2m';
const warmup = __ENV.WARMUP || '15s';
const maxVUs = Number(__ENV.MAX_VUS || 200);
const base = __ENV.BITWEEN_URL || 'http://bitween:8080';
const hasWarmup = !/^0+(s|m|ms)?$/.test(warmup);

let seeded = null;
try {
  seeded = JSON.parse(open('./results/seed.json'));
} catch (_) {
  seeded = null;
}

const latency = new Trend('sync_latency', true);
const failures = new Rate('sync_failed');
const answered = new Counter('sync_ok');

const scenarios = {
  load: {
    executor: 'constant-arrival-rate',
    rate,
    timeUnit: '1s',
    duration,
    preAllocatedVUs: Math.min(maxVUs, Math.max(10, rate * 2)),
    maxVUs,
    startTime: hasWarmup ? warmup : '0s',
    tags: { phase: 'load' },
  },
};
if (hasWarmup) {
  scenarios.warmup = {
    executor: 'constant-arrival-rate',
    rate,
    timeUnit: '1s',
    duration: warmup,
    preAllocatedVUs: Math.min(maxVUs, Math.max(10, rate * 2)),
    maxVUs,
    tags: { phase: 'warmup' },
  };
}

export const options = {
  scenarios,
  setupTimeout: '120s',
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  thresholds: {
    sync_latency: [
      `p(95)<${Number(__ENV.P95_BUDGET_MS || 3000)}`,
      `p(99)<${Number(__ENV.P99_BUDGET_MS || 5000)}`,
    ],
    sync_failed: [`rate<${Number(__ENV.ERROR_BUDGET || 0.01)}`],
  },
};

export function setup() {
  if (seeded) return seeded;
  return seed({
    base,
    email: __ENV.ADMIN_EMAIL || 'admin@Bitween.systems',
    password: __ENV.ADMIN_PASSWORD,
    partnerUrl: __ENV.PARTNER_URL || 'http://partner:8080/orders',
    runId: __ENV.RUN_ID,
  });
}

export default function (data) {
  const body = JSON.stringify({
    orderId: `${__VU}-${__ITER}`,
    customer: 'C-1001',
    lines: [{ sku: 'SKU-1', qty: 2, price: 12.5 }],
  });
  const res = http.post(`${base}/api/gateway/${data.urlName}/sync`, body, {
    headers: {
      'Content-Type': 'application/json',
      partnerkey: data.key,
      'Wait-Period': String(__ENV.WAIT_PERIOD || 30),
    },
    timeout: '60s',
    tags: { name: 'gateway sync' },
  });

  const good = check(res, { 'status is 200': (r) => r.status === 200 });
  // Only the measured phase feeds the budgets; the warm-up shows in the http_req_* metrics.
  if (exec.scenario.name === 'warmup') return;
  latency.add(res.timings.duration);
  failures.add(!good);
  if (good) answered.add(1);
}

export function handleSummary(data) {
  const m = data.metrics;
  const v = (name, stat) => (m[name] && m[name].values[stat] !== undefined ? m[name].values[stat] : NaN);
  const seconds = (() => {
    const d = String(duration).match(/^(\d+(?:\.\d+)?)(ms|s|m|h)$/);
    if (!d) return NaN;
    return Number(d[1]) * { ms: 0.001, s: 1, m: 60, h: 3600 }[d[2]];
  })();
  const okCount = v('sync_ok', 'count');
  const result = {
    rate,
    duration,
    warmup: hasWarmup ? warmup : '0s',
    calls: (v('sync_failed', 'passes') || 0) + (v('sync_failed', 'fails') || 0),
    ok: isNaN(okCount) ? 0 : okCount,
    exchangesPerSecond: (isNaN(okCount) ? 0 : okCount) / seconds,
    errorRate: v('sync_failed', 'rate'),
    latencyMs: {
      p50: v('sync_latency', 'med'),
      p95: v('sync_latency', 'p(95)'),
      p99: v('sync_latency', 'p(99)'),
      avg: v('sync_latency', 'avg'),
      max: v('sync_latency', 'max'),
    },
    droppedIterations: v('dropped_iterations', 'count') || 0,
    thresholdsPassed: Object.values(m).every((x) => !x.thresholds || Object.values(x.thresholds).every((t) => t.ok)),
  };
  const f = (n) => (isNaN(n) ? 'n/a' : n.toFixed(1));
  const text = [
    '',
    `Sync gateway, ${rate}/s for ${duration} (after ${result.warmup} warm-up)`,
    `  calls ${result.calls}, answered 200: ${result.ok}, error rate ${(100 * (result.errorRate || 0)).toFixed(2)}%`,
    `  latency ms  p50 ${f(result.latencyMs.p50)}  p95 ${f(result.latencyMs.p95)}  p99 ${f(result.latencyMs.p99)}  max ${f(result.latencyMs.max)}`,
    `  exchanges/s ${f(result.exchangesPerSecond)}  dropped iterations ${result.droppedIterations}`,
    `  thresholds ${result.thresholdsPassed ? 'passed' : 'FAILED'}`,
    '',
  ].join('\n');
  return {
    stdout: text,
    'results/summary.json': JSON.stringify(result, null, 2),
    'results/k6-full.json': JSON.stringify(data, null, 2),
  };
}
