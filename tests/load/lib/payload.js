// The order each sync call sends.
//
// PAYLOAD_KB unset or 0: the small order the harness has always sent. Set: an order with as many
// lines as it takes to reach about that many kilobytes (100 and 1024 are the usual sizes), built
// once for the whole run and shared by every virtual user, then sent with its own order id. Built
// per virtual user, a 1 MB order took k6 long enough to start one that it stopped starting them.
import { SharedArray } from 'k6/data';

const kb = Number(__ENV.PAYLOAD_KB || 0);
const placeholder = '__ORDER_ID__';

let template = null;

function line(i) {
  return {
    sku: `SKU-${String(i).padStart(6, '0')}`,
    qty: 1 + (i % 5),
    price: 12.5 + (i % 100) / 100,
    description: 'Standard item, boxed, with a description long enough to look like one',
    shippedOn: '2026-10-14T10:00:00Z',
  };
}

function build() {
  if (!kb) {
    return JSON.stringify({ orderId: placeholder, customer: 'C-1001', lines: [{ sku: 'SKU-1', qty: 2, price: 12.5 }] });
  }

  const order = { orderId: placeholder, customer: 'C-1001', total: 0, createdOn: '2026-10-14T10:00:00Z', lines: [] };
  const target = kb * 1024;
  let size = JSON.stringify(order).length;
  for (let i = 0; size < target; i++) {
    const next = line(i);
    order.lines.push(next);
    size += JSON.stringify(next).length + 1;
  }
  order.total = Math.round(order.lines.reduce((sum, l) => sum + l.qty * l.price, 0) * 100) / 100;
  return JSON.stringify(order);
}

const shared = new SharedArray('order', () => [build()]);

export function orderBody(orderId) {
  if (template === null) template = shared[0];
  return template.replace(placeholder, orderId);
}

export const payloadKb = kb;
