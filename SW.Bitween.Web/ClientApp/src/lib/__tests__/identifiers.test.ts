import { describe, it, expect } from 'vitest';
import { finishUrlName, toUrlName, urlNameProblem } from '../identifiers';

describe('toUrlName', () => {
    it('keeps / so a url name can run to several parts', () => {
        expect(toUrlName('Logistics/Slim/Orders')).toBe('logistics/slim/orders');
    });

    it('collapses repeated slashes and drops a leading one', () => {
        expect(toUrlName('/logistics//slim')).toBe('logistics/slim');
    });

    it('lets a trailing separator survive mid-typing', () => {
        expect(toUrlName('logistics/')).toBe('logistics/');
        expect(toUrlName('orders-')).toBe('orders-');
    });

    it('turns spaces, newlines and anything else a path cannot hold into a hyphen', () => {
        expect(toUrlName('Returns intake\n')).toBe('returns-intake-');
        expect(toUrlName('orders?v=2')).toBe('orders-v-2');
    });

    it('never lets separators double up or touch a slash', () => {
        expect(toUrlName('orders--v2')).toBe('orders-v2');
        expect(toUrlName('orders-_v2')).toBe('orders-v2');
        expect(toUrlName('logistics-/-slim')).toBe('logistics/slim');
    });
});

describe('urlNameProblem', () => {
    it('passes a name the API would take', () => {
        expect(urlNameProblem('logistics/slim/orders')).toBeNull();
        expect(urlNameProblem('sync/orders')).toBeNull();
    });

    it('refuses an empty name', () => {
        expect(urlNameProblem('')).not.toBeNull();
        expect(urlNameProblem('-/')).not.toBeNull();
    });

    it('refuses a last part of sync or async', () => {
        expect(urlNameProblem('orders/sync')).toMatch(/sync/);
        expect(urlNameProblem('orders/async/')).toMatch(/async/);
    });
});

describe('finishUrlName', () => {
    it('drops separators hugging a slash or ending the name', () => {
        expect(finishUrlName('logistics-/-slim/orders_/')).toBe('logistics/slim/orders');
    });
});
