import { describe, it, expect } from 'vitest';
import { generateScriban } from '../scribanGenerator';
import { parseScriban, resolveParentArrayIds } from '../scribanParser';
import { FieldMapping, ArrayMapping } from '../types';

// ─── Helpers ─────────────────────────────────────────────────────────────────

let _id = 0;
function id(): string { return `test-${++_id}`; }

function field(overrides: Partial<FieldMapping> & { target: string; source: string }): FieldMapping {
  return { id: id(), fixedValue: undefined, ...overrides };
}

function roundTrip(
  fieldMappings: FieldMapping[],
  arrayMappings: ArrayMapping[] = []
) {
  const template = generateScriban(fieldMappings, arrayMappings);
  const parsed = parseScriban(template);
  // Assign IDs so parentArrayId resolution works
  const withIds = parsed.arrayMappings.map((am) => ({ ...am, id: id() }));
  const resolvedAMs = resolveParentArrayIds(withIds) as Array<
    Omit<(typeof withIds)[number], 'parentTarget'> & { parentArrayId?: string }
  >;
  return {
    fieldMappings: parsed.fieldMappings,
    arrayMappings: resolvedAMs,
  };
}

// Strip id before comparing — parsed output never has one
function stripId(m: FieldMapping): Omit<FieldMapping, 'id'> {
  const { id: _id, ...rest } = m;
  return rest;
}

// ─── Source mappings ──────────────────────────────────────────────────────────

describe('round-trip: source mappings', () => {
  it('renames a flat field', () => {
    const fm = [field({ target: 'orderId', source: 'Order.Id' })];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings).toHaveLength(1);
    expect(fieldMappings[0].target).toBe('orderId');
    expect(fieldMappings[0].source).toBe('Order.Id');
  });

  it('round-trips multiple flat fields', () => {
    const fm = [
      field({ target: 'a', source: 'x' }),
      field({ target: 'b', source: 'y' }),
    ];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings.map((m) => stripId(m as FieldMapping))).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ target: 'a', source: 'x' }),
        expect.objectContaining({ target: 'b', source: 'y' }),
      ])
    );
  });
});

// ─── Fixed value mappings ─────────────────────────────────────────────────────

describe('round-trip: fixed value mappings', () => {
  it('string fixed value', () => {
    const fm = [field({ target: 'status', source: '', fixedValue: 'active' })];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings[0]).toMatchObject({ target: 'status', fixedValue: 'active' });
  });

  it('numeric fixed value', () => {
    const fm = [field({ target: 'count', source: '', fixedValue: '42' })];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings[0]).toMatchObject({ target: 'count', fixedValue: '42' });
  });

  it('boolean fixed value true', () => {
    const fm = [field({ target: 'flag', source: '', fixedValue: 'true' })];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings[0]).toMatchObject({ target: 'flag', fixedValue: 'true' });
  });
});

// ─── Partner / global mappings ────────────────────────────────────────────────

describe('round-trip: partner and global mappings', () => {
  it('partner prop key', () => {
    const fm = [field({ target: 'pkey', source: '', partnerPropKey: 'apiKey' })];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings[0]).toMatchObject({ target: 'pkey', partnerPropKey: 'apiKey' });
  });

  it('global set key', () => {
    const fm = [field({ target: 'gval', source: '', globalSetId: 'mySet', globalKey: 'region' })];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings[0]).toMatchObject({ target: 'gval', globalSetId: 'mySet', globalKey: 'region' });
  });
});

// ─── Lookup dictionary mappings ───────────────────────────────────────────────

describe('round-trip: lookup dictionary', () => {
  it('null fallback', () => {
    const fm = [field({
      target: 'category',
      source: 'Cat',
      lookupDictionary: {
        entries: [{ from: 'A', to: 'Alpha' }, { from: 'B', to: 'Beta' }],
        fallback: 'null',
      },
    })];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings[0].lookupDictionary?.fallback).toBe('null');
    expect(fieldMappings[0].lookupDictionary?.entries).toEqual([
      { from: 'A', to: 'Alpha' },
      { from: 'B', to: 'Beta' },
    ]);
  });

  it('custom fallback', () => {
    const fm = [field({
      target: 'category',
      source: 'Cat',
      lookupDictionary: {
        entries: [{ from: 'A', to: 'Alpha' }],
        fallback: 'custom',
        fallbackValue: 'Unknown',
      },
    })];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings[0].lookupDictionary?.fallback).toBe('custom');
    expect(fieldMappings[0].lookupDictionary?.fallbackValue).toBe('Unknown');
  });
});

// ─── Transform mappings ───────────────────────────────────────────────────────

describe('round-trip: transform', () => {
  it('arithmetic transform', () => {
    const fm = [field({ target: 'doubled', source: 'amount', transform: 'value * 2' })];
    const { fieldMappings } = roundTrip(fm);
    expect(fieldMappings[0]).toMatchObject({ target: 'doubled', source: 'amount' });
    // transform is present (exact expression may differ — just verify it round-trips without null)
    expect(fieldMappings[0].transform).toBeTruthy();
  });
});

// ─── Array mappings ───────────────────────────────────────────────────────────

describe('round-trip: array mappings', () => {
  it('simple object array', () => {
    const am: ArrayMapping[] = [{
      id: id(),
      source: 'Items',
      target: 'lines',
      alias: 'item',
      mappings: [
        { id: id(), target: 'sku', source: 'Sku' },
        { id: id(), target: 'qty', source: 'Quantity' },
      ],
    }];
    const { arrayMappings } = roundTrip([], am);
    expect(arrayMappings).toHaveLength(1);
    expect(arrayMappings[0]).toMatchObject({ source: 'Items', target: 'lines' });
    expect(arrayMappings[0].mappings).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ target: 'sku', source: 'Sku' }),
        expect.objectContaining({ target: 'qty', source: 'Quantity' }),
      ])
    );
  });

  it('array with filter', () => {
    const am: ArrayMapping[] = [{
      id: id(),
      source: 'Items',
      target: 'active',
      alias: 'item',
      filter: { field: 'Status', operator: '==', value: 'active' },
      mappings: [{ id: id(), target: 'name', source: 'Name' }],
    }];
    const { arrayMappings } = roundTrip([], am);
    expect(arrayMappings[0].filter).toMatchObject({ field: 'Status', operator: '==', value: 'active' });
  });

  it('nested array (two levels)', () => {
    const parentId = id();
    const am: ArrayMapping[] = [
      {
        id: parentId,
        source: 'Orders',
        target: 'orders',
        alias: 'ord',
        mappings: [{ id: id(), target: 'ref', source: 'Ref' }],
      },
      {
        id: id(),
        parentArrayId: parentId,
        source: 'Lines',
        target: 'lines',
        alias: 'ln',
        mappings: [{ id: id(), target: 'sku', source: 'Sku' }],
      },
    ];
    const { arrayMappings } = roundTrip([], am);
    expect(arrayMappings).toHaveLength(2);
    const parent = arrayMappings.find((a) => a.target === 'orders');
    const child = arrayMappings.find((a) => a.target === 'lines');
    expect(parent).toBeDefined();
    expect(child).toBeDefined();
    expect(child?.parentArrayId).toBe(parent?.id);
  });
});
