// Creates, through Bitween's admin API, what the sync load test calls: an information type, a
// partner with a key, a gateway subscription whose handler is the native HTTP handler pointed at
// the partner endpoint (no mapper unless MAPPER says, no notifications), and an API gateway with
// the partner attached by key. Every name carries a run id, so seeding again creates a fresh set
// beside the old one instead of failing on a name that is taken.
//
// MAPPER: unset for none; "rules" for the rules-based mapper (NativeMapper) mapping the header and
// every line; "scriban" for the Scriban mapper (NativeJSONMapper) mapping the header, the line
// count and the first line. Finding 6 measures both against large payloads; a Scriban loop stops
// at 1000 iterations, so its template does not walk the lines.
import http from 'k6/http';

const path = (p) => ({ kind: 'Path', path: p });

const mappers = {
  rules: {
    mapperId: 'NativeMapper',
    mapperProperties: [{
      key: 'MappingRules',
      value: JSON.stringify({
        sourceFormat: 'json',
        targetFormat: 'json',
        fields: [
          { target: ['reference'], from: path('orderId') },
          { target: ['customer'], from: path('customer') },
          { target: ['total'], from: path('total'), type: 'Number' },
          { target: ['createdOn'], from: path('createdOn') },
        ],
        lists: [{
          over: 'lines', as: 'line', target: ['lines'],
          fields: [
            { target: ['sku'], from: path('sku') },
            { target: ['quantity'], from: path('qty'), type: 'Number' },
            { target: ['unitPrice'], from: path('price'), type: 'Number' },
          ],
        }],
      }),
    }],
  },
  scriban: {
    mapperId: 'NativeJSONMapper',
    mapperProperties: [{
      key: 'ScribanTemplate',
      value: '{ "reference": {{ orderId | json }}, "customer": {{ customer | json }}, "total": {{ total | json }}, ' +
        '"createdOn": {{ createdOn | json }}, "lineCount": {{ lines | array.size }}, "first": {{ lines[0] | json }} }',
    }],
  },
};

const json = { 'Content-Type': 'application/json' };

function fail(what, res) {
  throw new Error(`${what}: HTTP ${res.status} ${String(res.body).slice(0, 500)}`);
}

function ok(what, res) {
  if (res.status < 200 || res.status > 299) fail(what, res);
  return res;
}

function signIn(base, email, password) {
  const res = http.post(`${base}/api/accounts/login`, JSON.stringify({ username: email, password }), { headers: json });
  return res.status === 200 ? res.json() : null;
}

// The seeded administrator must change its password at first sign-in, and its token grants
// nothing until it does. Changing it to the same value clears the flag.
function adminToken(base, email, password) {
  const first = signIn(base, email, password);
  if (!first) throw new Error(`sign-in as ${email} failed; check ADMIN_PASSWORD (Bitween__InitialAdminPassword)`);
  if (!first.mustChangePassword) return first.jwt;

  const auth = { ...json, Authorization: `Bearer ${first.jwt}` };
  ok('change password', http.post(`${base}/api/accounts/changePassword`,
    JSON.stringify({ oldPassword: password, newPassword: password }), { headers: auth }));
  const second = signIn(base, email, password);
  if (!second || second.mustChangePassword) throw new Error('password change did not clear the must-change flag');
  return second.jwt;
}

export function seed({ base, email, password, partnerUrl, runId, mapper }) {
  if (mapper && !mappers[mapper]) throw new Error(`MAPPER=${mapper}: use rules, scriban, or leave it unset`);
  const id = runId || `${Date.now().toString(36)}${Math.floor(Math.random() * 1e6).toString(36)}`;
  const headers = { ...json, Authorization: `Bearer ${adminToken(base, email, password)}` };
  const post = (what, path, body) => ok(what, http.post(`${base}${path}`, JSON.stringify(body), { headers }));
  const created = (what, path, body) => Number(post(what, path, body).body);

  const documentId = created('create information type', '/api/documents',
    { name: `Load order ${id}`, documentFormat: 'Json' });

  const partnerId = created('create partner', '/api/partners', { name: `Load partner ${id}` });

  const key = ok('generate key', http.get(`${base}/api/partners/generatekey`, { headers })).body.replace(/^"|"$/g, '');
  // Keys are saved the way the partner page saves them: the whole record, keys replaced.
  const partner = ok('read partner', http.get(`${base}/api/partners/${partnerId}`, { headers })).json();
  post('save partner key', `/api/partners/${partnerId}`, {
    name: partner.name,
    adapterProperties: partner.adapterProperties,
    secretProperties: partner.secretProperties,
    loginIdentity: partner.loginIdentity,
    apiCredentials: [{ key: 'load', value: key }],
  });

  const subscriptionId = created('create subscription', '/api/subscriptions', {
    name: `Load sync ${id}`,
    documentId,
    type: 'GatewayApiCall',
    inactive: false,
    ...(mapper ? mappers[mapper] : {}),
    handlerId: 'NativeHttpHandler',
    handlerProperties: [
      { key: 'Url', value: partnerUrl },
      { key: 'Verb', value: 'post' },
      { key: 'ContentType', value: 'application/json' },
      { key: 'TimeoutSeconds', value: '10' },
    ],
    notifications: [],
  });

  const urlName = `load-${id}`.toLowerCase().slice(0, 40);
  const gatewayId = created('create gateway', '/api/apigateways', { name: `Load gateway ${id}`, urlName, inactive: false });
  post('attach partner to gateway', `/api/apigateways/${gatewayId}/addpartner`, { partnerId, subscriptionId });

  return { runId: id, urlName, key, documentId, partnerId, subscriptionId, gatewayId };
}
