/**
 * Generator output snapshot tests.
 * These freeze exactly what template string generateScriban produces for each
 * mapping config. Snapshots are stored in __snapshots__/. A failure here means
 * the generator changed its output — check whether the C# parity tests also
 * need updating (ScribanGeneratorParityTests.cs).
 */
import { describe, it, expect } from 'vitest';
import { generateScriban } from '../scribanGenerator';
import { FieldMapping, ArrayMapping } from '../types';

let _id = 0;
function id() { return `t-${++_id}`; }
function field(o: Partial<FieldMapping> & { target: string; source: string }): FieldMapping {
  return { id: id(), ...o };
}

describe('generator output snapshots', () => {
  it('source field rename', () => {
    const t = generateScriban([field({ target: 'orderId', source: 'Order.Id' })], []);
    expect(t).toMatchInlineSnapshot(`
      "{
        "orderId": {{ Order.Id | json }},
      }"
    `);
  });

  it('fixed string value', () => {
    const t = generateScriban([field({ target: 'status', source: '', fixedValue: 'active' })], []);
    expect(t).toMatchInlineSnapshot(`
      "{
        "status": "active",
      }"
    `);
  });

  it('fixed number value', () => {
    const t = generateScriban([field({ target: 'count', source: '', fixedValue: '42' })], []);
    expect(t).toMatchInlineSnapshot(`
      "{
        "count": 42,
      }"
    `);
  });

  it('fixed boolean value', () => {
    const t = generateScriban([field({ target: 'flag', source: '', fixedValue: 'true' })], []);
    expect(t).toMatchInlineSnapshot(`
      "{
        "flag": true,
      }"
    `);
  });

  it('partner property', () => {
    const t = generateScriban([field({ target: 'pkey', source: '', partnerPropKey: 'apiKey' })], []);
    expect(t).toMatchInlineSnapshot(`
      "{
        "pkey": {{ (__partner__ ?? {})["apiKey"] | json }},
      }"
    `);
  });

  it('global set key', () => {
    const t = generateScriban([field({ target: 'gval', source: '', globalSetId: 'mySet', globalKey: 'region' })], []);
    expect(t).toMatchInlineSnapshot(`
      "{
        "gval": {{ ((__globals__ ?? {})["mySet"] ?? {})["region"] | json }},
      }"
    `);
  });

  it('lookup null fallback', () => {
    const t = generateScriban([field({
      target: 'category', source: 'Cat',
      lookupDictionary: { entries: [{ from: 'A', to: 'Alpha' }], fallback: 'null' },
    })], []);
    expect(t).toMatchInlineSnapshot(`
      "{
        "category": {{ $__e = { "A": "Alpha" }; $__e[Cat] | json }},
      }"
    `);
  });

  it('lookup custom fallback', () => {
    const t = generateScriban([field({
      target: 'category', source: 'Cat',
      lookupDictionary: { entries: [{ from: 'A', to: 'Alpha' }], fallback: 'custom', fallbackValue: 'Unknown' },
    })], []);
    expect(t).toMatchInlineSnapshot(`
      "{
        "category": {{ $__e = { "A": "Alpha" }; ($__e[Cat] ?? "Unknown") | json }},
      }"
    `);
  });

  it('transform', () => {
    const t = generateScriban([field({ target: 'doubled', source: 'amount', transform: 'value * 2' })], []);
    expect(t).toMatchInlineSnapshot(`
      "{
        "doubled": {{ amount * 2 | json }},
      }"
    `);
  });

  it('object array mapping', () => {
    const am: ArrayMapping[] = [{
      id: id(), source: 'Items', target: 'lines', alias: 'item',
      mappings: [
        { id: id(), target: 'sku', source: 'Sku' },
        { id: id(), target: 'qty', source: 'Quantity' },
      ],
    }];
    expect(generateScriban([], am)).toMatchInlineSnapshot(`
      "{
        "lines": [
        {{- for item in Items -}}
        {
          "sku": {{ item.Sku | json }},
          "qty": {{ item.Quantity | json }},
        },
        {{- end -}}
        ],
      }"
    `);
  });

  it('array with filter', () => {
    const am: ArrayMapping[] = [{
      id: id(), source: 'Items', target: 'active', alias: 'item',
      filter: { field: 'Status', operator: '==', value: 'active' },
      mappings: [{ id: id(), target: 'name', source: 'Name' }],
    }];
    expect(generateScriban([], am)).toMatchInlineSnapshot(`
      "{
        "active": [
        {{- for item in Items -}}
        {{- if item.Status == "active" -}}
        {
          "name": {{ item.Name | json }},
        },
        {{- end -}}
        {{- end -}}
        ],
      }"
    `);
  });

  // ── Type-cast rules (requires outputJson + inputJson so generator knows types) ──

  it('type rule: bool source → string target', () => {
    const t = generateScriban(
      [field({ target: 'result', source: 'flag' })], [],
      undefined, '{"result": ""}', '{"flag": true}',
    );
    expect(t).toMatchInlineSnapshot(`
      "{
        "result": {{ (flag == null ? null : (flag ? "true" : "false")) | json }},
      }"
    `);
  });

  it('type rule: number source → string target', () => {
    const t = generateScriban(
      [field({ target: 'result', source: 'count' })], [],
      undefined, '{"result": ""}', '{"count": 42}',
    );
    expect(t).toMatchInlineSnapshot(`
      "{
        "result": {{ ("" + count) | json }},
      }"
    `);
  });

  it('type rule: string source → number target (to_float)', () => {
    const t = generateScriban(
      [field({ target: 'result', source: 'price' })], [],
      undefined, '{"result": 0}', '{"price": "9.99"}',
    );
    expect(t).toMatchInlineSnapshot(`
      "{
        "result": {{ (price | to_float) | json }},
      }"
    `);
  });

  it('type rule: bool source → number target', () => {
    const t = generateScriban(
      [field({ target: 'result', source: 'flag' })], [],
      undefined, '{"result": 0}', '{"flag": true}',
    );
    expect(t).toMatchInlineSnapshot(`
      "{
        "result": {{ (flag == null ? null : (flag ? 1 : 0)) | json }},
      }"
    `);
  });

  it('type rule: number source → bool target', () => {
    const t = generateScriban(
      [field({ target: 'result', source: 'amount' })], [],
      undefined, '{"result": false}', '{"amount": 1}',
    );
    expect(t).toMatchInlineSnapshot(`
      "{
        "result": {{ (amount == 0 ? false : (amount == 1 ? true : null)) | json }},
      }"
    `);
  });

  it('transform with string target type', () => {
    const t = generateScriban(
      [field({ target: 'result', source: 'price', transform: 'value * 2' })], [],
      undefined, '{"result": ""}', '{"price": 5}',
    );
    expect(t).toMatchInlineSnapshot(`
      "{
        "result": {{ $__t = (price * 2); ($__t == null ? null : ((($__t | object.typeof) == "object" || ($__t | object.typeof) == "array") ? null : (($__t | object.typeof) == "boolean" ? ($__t ? "true" : "false") : ("" + $__t)))) | json }},
      }"
    `);
  });

  it('transform with number target type', () => {
    const t = generateScriban(
      [field({ target: 'result', source: 'amount', transform: 'value > 100' })], [],
      undefined, '{"result": 0}', '{"amount": 200}',
    );
    expect(t).toMatchInlineSnapshot(`
      "{
        "result": {{ $__t = (amount > 100); ($__t == null ? null : (($__t | object.typeof) == "boolean" ? ($__t ? 1 : 0) : ((($__t | object.typeof) == "object" || ($__t | object.typeof) == "array") ? null : ($__t | to_float)))) | json }},
      }"
    `);
  });

  it('isRootSource: field inside array loop reads from root object', () => {
    const am: ArrayMapping[] = [{
      id: id(), source: 'Items', target: 'lines', alias: 'item',
      mappings: [
        { id: id(), target: 'sku', source: 'Sku' },
        { id: id(), target: 'orderId', source: 'OrderId', isRootSource: true },
      ],
    }];
    expect(generateScriban([], am)).toMatchInlineSnapshot(`
      "{
        "lines": [
        {{- for item in Items -}}
        {
          "sku": {{ item.Sku | json }},
          "orderId": {{ OrderId | json }},
        },
        {{- end -}}
        ],
      }"
    `);
  });
});
