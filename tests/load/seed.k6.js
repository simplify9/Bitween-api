// Seeds the objects the sync test calls and writes them to results/seed.json, which sync.k6.js
// reads. Then primes the new gateway with PRIME (default 30) sync calls, one at a time, so the
// measured run starts on a warm node: compiled code, loaded caches, open connections. Run on its
// own so neither the seeding nor the priming is in the measured window:
//   docker compose run --rm k6 run seed.k6.js
//
// Why priming: on a node fresh from start, the first trial run at 10/s collapsed (p95 42 s,
// 37% failed, Npgsql timing out waiting for a pooled connection) while the same rate on the
// warmed node answered every call at p95 1.1 s. A short warm-up at full rate was not enough.
import http from 'k6/http';
import { check } from 'k6';
import { seed } from './lib/seed.js';
import { orderBody, payloadKb } from './lib/payload.js';

const prime = Number(__ENV.PRIME || 30);

export const options = {
  vus: 1,
  iterations: Math.max(1, prime),
  setupTimeout: '120s',
  thresholds: {},
};

export function setup() {
  return seed({
    base: __ENV.BITWEEN_URL || 'http://bitween:8080',
    email: __ENV.ADMIN_EMAIL || 'admin@Bitween.systems',
    password: __ENV.ADMIN_PASSWORD,
    partnerUrl: __ENV.PARTNER_URL || 'http://partner:8080/orders',
    runId: __ENV.RUN_ID,
    mapper: __ENV.MAPPER,
  });
}

export default function (data) {
  if (prime <= 0) return;
  const res = http.post(`${__ENV.BITWEEN_URL || 'http://bitween:8080'}/api/gateway/${data.urlName}/sync`,
    payloadKb ? orderBody(`prime-${__ITER}`) : JSON.stringify({ orderId: `prime-${__ITER}` }),
    { headers: { 'Content-Type': 'application/json', partnerkey: data.key, 'Wait-Period': '30' }, timeout: '60s' });
  check(res, { 'priming call answered 200': (r) => r.status === 200 });
}

export function handleSummary(data) {
  const seeded = data.setup_data;
  return {
    stdout: `seeded gateway /api/gateway/${seeded.urlName}/sync (subscription ${seeded.subscriptionId}, partner ${seeded.partnerId}); ` +
      `priming calls answered 200: ${data.metrics.checks ? data.metrics.checks.values.passes : 0} of ${prime}\n`,
    'results/seed.json': JSON.stringify(seeded, null, 2),
  };
}
