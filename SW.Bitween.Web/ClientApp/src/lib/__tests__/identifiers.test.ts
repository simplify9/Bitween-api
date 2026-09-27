import { describe, it, expect } from 'vitest';
import { finishUrlName, toUrlName } from '../identifiers';

describe('toUrlName', () => {
    it('keeps / so a url name can run to several parts', () => {
        expect(toUrlName('Logistics/Slim/Orders')).toBe('logistics/slim/orders');
    });

    it('collapses repeated slashes and drops a leading one', () => {
        expect(toUrlName('/logistics//slim')).toBe('logistics/slim');
    });

    it('lets a trailing separator survive mid-typing', () => {
        expect(toUrlName('logistics/')).toBe('logistics/');
    });
});

describe('finishUrlName', () => {
    it('drops separators hugging a slash or ending the name', () => {
        expect(finishUrlName('logistics-/-slim/orders_/')).toBe('logistics/slim/orders');
    });
});
