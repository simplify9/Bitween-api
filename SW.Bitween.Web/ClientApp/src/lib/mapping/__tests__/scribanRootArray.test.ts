/**
 * Root-array mapping tests.
 * Covers:
 *  A) Generator output — verify generated Scriban template strings
 *  B) Parser round-trip — generate → parse → re-generate produces equivalent output
 *  C) All 4 modes (source / fixed / partner / global) + lookup + transform
 *  D) Primitive root arrays
 *  E) Filter support
 *  F) Nested arrays inside root-array items
 */
import { generateScriban } from '../scribanGenerator';
import { parseScriban, resolveParentArrayIds } from '../scribanParser';
import { FieldMapping, ArrayMapping } from '../types';

// ─── Helpers ─────────────────────────────────────────────────────────────────

let _id = 0;
function id() { return `t-${++_id}`; }

function am(overrides: Partial<ArrayMapping> & { source: string; alias: string }): ArrayMapping {
  return {
    id: id(),
    target: '',
    mappings: [],
    isRootOutput: true,
    ...overrides,
  };
}

function field(o: Partial<FieldMapping> & { target: string; source: string }): FieldMapping {
  return { id: id(), ...o };
}

function roundTrip(arrayMappings: ArrayMapping[]) {
  const template = generateScriban([], arrayMappings);
  const parsed = parseScriban(template);
  const withIds = parsed.arrayMappings.map((a) => ({ ...a, id: id() }));
  const resolved = resolveParentArrayIds(withIds);
  return { template, parsed, arrayMappings: resolved };
}

// ─── A. Generator output ─────────────────────────────────────────────────────

describe('generator: root-array output', () => {
  it('emits root [ ] when isRootOutput=true', () => {
    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'id', source: 'Id' })],
    })]);

    expect(t.trim()).toMatch(/^\[/);
    expect(t.trim()).toMatch(/\]$/);
    expect(t).toContain('for item in items');
    expect(t).toContain('"id": {{ item.Id | json }}');
  });

  it('does NOT emit wrapping { } object', () => {
    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'id', source: 'Id' })],
    })]);

    expect(t.trim()).not.toMatch(/^\{/);
  });

  it('fieldMappings are ignored when isRootOutput AM is present', () => {
    const t = generateScriban(
      [field({ target: 'shouldBeIgnored', source: 'x' })],
      [am({ source: 'items', alias: 'item', mappings: [] })],
    );

    expect(t).not.toContain('shouldBeIgnored');
  });

  it('source mode: item field reference', () => {
    const t = generateScriban([], [am({
      source: 'orders', alias: 'order',
      mappings: [field({ target: 'orderId', source: 'OrderId' })],
    })]);

    expect(t).toContain('"orderId": {{ order.OrderId | json }}');
  });

  it('fixed mode: string value', () => {
    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'type', source: '', fixedValue: 'order' })],
    })]);

    expect(t).toContain('"type": "order"');
  });

  it('fixed mode: numeric value', () => {
    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'version', source: '', fixedValue: '42' })],
    })]);

    expect(t).toContain('"version": 42');
  });

  it('partner mode', () => {
    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'env', source: '', partnerPropKey: 'environment' })],
    })]);

    expect(t).toContain('"env": {{ __partner__?.environment | json }}');
  });

  it('global mode', () => {
    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'region', source: '', globalSetId: 'mySet', globalKey: 'region' })],
    })]);

    expect(t).toContain('"region": {{ __globals__?.mySet["region"] | json }}');
  });

  it('lookup — null fallback', () => {
    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({
        target: 'label', source: 'Status',
        lookupDictionary: { entries: [{ from: 'A', to: 'Active' }], fallback: 'null' },
      })],
    })]);

    expect(t).toContain('$__e = { "A": "Active" }');
    expect(t).toContain('$__e[item.Status]');
  });

  it('lookup — custom fallback', () => {
    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({
        target: 'label', source: 'Code',
        lookupDictionary: { entries: [{ from: 'A', to: 'Alpha' }], fallback: 'custom', fallbackValue: 'Unknown' },
      })],
    })]);

    expect(t).toContain('"Unknown"');
    expect(t).toContain('??');
  });

  it('filter — emits if block inside loop', () => {
    const t = generateScriban([], [am({
      source: 'orders', alias: 'order',
      filter: { field: 'Active', operator: '==', value: 'true' },
      mappings: [field({ target: 'id', source: 'Id' })],
    })]);

    expect(t).toContain('if order.Active == "true"');
  });

  it('primitive root array via source loop', () => {
    const t = generateScriban([], [{
      id: id(),
      target: '',
      source: 'tags',
      alias: 'tag',
      isRootOutput: true,
      mappings: [],
      primitiveItems: [{ source: 'tags' }],
    }]);

    expect(t.trim()).toMatch(/^\[/);
    // primitive items are emitted as raw {{ path | json }} lines
    expect(t).toContain('{{ tags | json }}');
  });
});

// ─── B. Parser round-trip ────────────────────────────────────────────────────

describe('parser: root-array template round-trip', () => {
  it('detects isRootOutput=true when template starts with [', () => {
    const template = `[
{{- for item in items -}}
{
  "id": {{ item.Id | json }},
},
{{- end -}}
]`;
    const { arrayMappings } = parseScriban(template);

    expect(arrayMappings).toHaveLength(1);
    expect(arrayMappings[0].isRootOutput).toBe(true);
    expect(arrayMappings[0].source).toBe('items');
    expect(arrayMappings[0].alias).toBe('item');
    expect(arrayMappings[0].target).toBe('');
  });

  it('parses field mappings inside the root loop', () => {
    const template = `[
{{- for order in orders -}}
{
  "orderId": {{ order.OrderId | json }},
  "status": "ACTIVE",
},
{{- end -}}
]`;
    const { arrayMappings } = parseScriban(template);

    expect(arrayMappings[0].mappings).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ target: 'orderId', source: 'OrderId' }),
        expect.objectContaining({ target: 'status', fixedValue: 'ACTIVE' }),
      ])
    );
  });

  it('round-trips source mapping through generate → parse', () => {
    const original: ArrayMapping[] = [am({
      source: 'orders', alias: 'order',
      mappings: [
        field({ target: 'id', source: 'OrderId' }),
        field({ target: 'status', source: '', fixedValue: 'pending' }),
      ],
    })];

    const { arrayMappings } = roundTrip(original);

    expect(arrayMappings).toHaveLength(1);
    expect(arrayMappings[0].isRootOutput).toBe(true);
    expect(arrayMappings[0].source).toBe('orders');
    expect(arrayMappings[0].mappings).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ target: 'id', source: 'OrderId' }),
        expect.objectContaining({ target: 'status', fixedValue: 'pending' }),
      ])
    );
  });

  it('round-trips partner mapping', () => {
    const original: ArrayMapping[] = [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'env', source: '', partnerPropKey: 'environment' })],
    })];

    const { arrayMappings } = roundTrip(original);

    expect(arrayMappings[0].mappings[0]).toMatchObject({ target: 'env', partnerPropKey: 'environment' });
  });

  it('round-trips global mapping', () => {
    const original: ArrayMapping[] = [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'region', source: '', globalSetId: 'mySet', globalKey: 'region' })],
    })];

    const { arrayMappings } = roundTrip(original);

    expect(arrayMappings[0].mappings[0]).toMatchObject({ globalSetId: 'mySet', globalKey: 'region' });
  });

  it('round-trips lookup with null fallback', () => {
    const original: ArrayMapping[] = [am({
      source: 'items', alias: 'item',
      mappings: [field({
        target: 'label', source: 'Code',
        lookupDictionary: { entries: [{ from: 'A', to: 'Alpha' }, { from: 'B', to: 'Beta' }], fallback: 'null' },
      })],
    })];

    const { arrayMappings } = roundTrip(original);
    const m = arrayMappings[0].mappings[0];

    expect(m.lookupDictionary?.fallback).toBe('null');
    expect(m.lookupDictionary?.entries).toEqual([
      { from: 'A', to: 'Alpha' },
      { from: 'B', to: 'Beta' },
    ]);
  });

  it('round-trips lookup with custom fallback', () => {
    const original: ArrayMapping[] = [am({
      source: 'items', alias: 'item',
      mappings: [field({
        target: 'label', source: 'Code',
        lookupDictionary: { entries: [{ from: 'A', to: 'Alpha' }], fallback: 'custom', fallbackValue: 'Unknown' },
      })],
    })];

    const { arrayMappings } = roundTrip(original);
    const m = arrayMappings[0].mappings[0];

    expect(m.lookupDictionary?.fallback).toBe('custom');
    expect(m.lookupDictionary?.fallbackValue).toBe('Unknown');
  });

  it('does NOT return fieldMappings for a root-array template', () => {
    const template = `[
{{- for item in items -}}
{
  "id": {{ item.Id | json }},
},
{{- end -}}
]`;
    const { fieldMappings } = parseScriban(template);

    expect(fieldMappings).toHaveLength(0);
  });

  it('normal (non-root-array) template is unaffected', () => {
    const template = `{
  "orderId": {{ orderId | json }},
}`;
    const { fieldMappings, arrayMappings } = parseScriban(template);

    expect(fieldMappings).toHaveLength(1);
    expect(fieldMappings[0]).toMatchObject({ target: 'orderId', source: 'orderId' });
    expect(arrayMappings).toHaveLength(0);
  });

  it('generates template that starts with [ when isRootOutput=true and re-parsed produces same AM count', () => {
    const original: ArrayMapping[] = [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'x', source: 'X' })],
    })];
    const t1 = generateScriban([], original);
    const { arrayMappings: parsed1 } = parseScriban(t1);
    const withIds = parsed1.map((a) => ({ ...a, id: id() }));
    const t2 = generateScriban([], withIds.map((a) => ({ ...a, mappings: a.mappings.map((m) => ({ ...m, id: id() })) })));

    // Both templates should start with [
    expect(t1.trim()[0]).toBe('[');
    expect(t2.trim()[0]).toBe('[');
    // Both should contain the same field reference
    expect(t1).toContain('item.X');
    expect(t2).toContain('item.X');
  });
});

// ─── G. Type casting with root-array output ──────────────────────────────────

describe('type casting: root-array output', () => {
  it('string source → number target applies to_float', () => {
    const inputJson = JSON.stringify({ items: [{ price: '19.99' }] });
    const outputJson = JSON.stringify([{ total: 0.0 }]);

    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'total', source: 'price' })],
    })], undefined, outputJson, inputJson);

    expect(t).toContain('to_float');
  });

  it('number source → string target applies "" + cast', () => {
    const inputJson = JSON.stringify({ items: [{ qty: 5 }] });
    const outputJson = JSON.stringify([{ qty: '' }]);

    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'qty', source: 'qty' })],
    })], undefined, outputJson, inputJson);

    expect(t).toMatch(/"qty":.*"" \+/);
  });

  it('same type (number → number) emits no cast', () => {
    const inputJson = JSON.stringify({ items: [{ id: 1 }] });
    const outputJson = JSON.stringify([{ id: 0 }]);

    const t = generateScriban([], [am({
      source: 'items', alias: 'item',
      mappings: [field({ target: 'id', source: 'id' })],
    })], undefined, outputJson, inputJson);

    expect(t).toContain('"id": {{ item.id | json }}');
    expect(t).not.toContain('to_float');
  });

  it('nested array: number source → string target applies "" + cast', () => {
    const inputJson = JSON.stringify([{ OrderId: 1, LineItems: [{ SKU: 'ABC', Qty: 2 }] }]);
    const outputJson = JSON.stringify([{ orderId: 0, lines: [{ sku: '', qty: '0' }] }]);

    const rootAm: ArrayMapping = {
      id: 'root', isRootOutput: true, source: 'items', alias: 'item', target: '',
      mappings: [field({ target: 'orderId', source: 'OrderId' })],
    };
    const linesAm: ArrayMapping = {
      id: 'lines', parentArrayId: 'root', source: 'LineItems', alias: 'line', target: 'lines',
      mappings: [
        field({ target: 'sku', source: 'SKU' }),
        field({ target: 'qty', source: 'Qty' }),
      ],
    };

    const t = generateScriban([], [rootAm, linesAm], undefined, outputJson, inputJson);

    // qty target is string, Qty source is number → must cast
    // qty target is string, Qty source is number → must cast with "" +
    expect(t).toMatch(/"qty":[\s\S]*"" \+[\s\S]*Qty/);
  });
});
