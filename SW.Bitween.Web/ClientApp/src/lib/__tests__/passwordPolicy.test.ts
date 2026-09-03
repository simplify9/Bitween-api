import { describe, it, expect } from 'vitest';
import { validatePassword } from '../passwordPolicy';

const TOO_SHORT = 'Password must be at least 8 characters.';
const NO_UPPER = 'Password must contain an uppercase letter.';
const NO_LOWER = 'Password must contain a lowercase letter.';
const NO_NUMBER = 'Password must contain a number.';
const NO_SPECIAL = 'Password must contain a special character.';

describe('validatePassword', () => {
    it('reports every rule for undefined', () => {
        expect(validatePassword(undefined)).toEqual([
            TOO_SHORT, NO_UPPER, NO_LOWER, NO_NUMBER, NO_SPECIAL,
        ]);
    });

    it('reports every rule for an empty string, in order', () => {
        expect(validatePassword('')).toEqual([
            TOO_SHORT, NO_UPPER, NO_LOWER, NO_NUMBER, NO_SPECIAL,
        ]);
    });

    it('rejects 7 characters and accepts 8 (length boundary)', () => {
        expect(validatePassword('Abc1!de')).toEqual([TOO_SHORT]); // 7 chars, all classes present
        expect(validatePassword('Abc1!def')).toEqual([]);          // 8 chars
    });

    it('flags a missing uppercase letter', () => {
        expect(validatePassword('abc1!def')).toEqual([NO_UPPER]);
    });

    it('flags a missing lowercase letter', () => {
        expect(validatePassword('ABC1!DEF')).toEqual([NO_LOWER]);
    });

    it('flags a missing number', () => {
        expect(validatePassword('Abcd!efg')).toEqual([NO_NUMBER]);
    });

    it('flags a missing special character', () => {
        expect(validatePassword('Abcd1efg')).toEqual([NO_SPECIAL]);
    });

    it('accepts a valid password', () => {
        expect(validatePassword('Str0ng!Pass')).toEqual([]);
    });
});
