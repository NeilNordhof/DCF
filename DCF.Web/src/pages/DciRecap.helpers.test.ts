import { describe, it, expect } from 'vitest';
import { computeRanks } from './DciRecap.helpers';

describe('computeRanks', () => {
  it('ranks higher values as 1st by default (higher is better)', () => {
    const items = [{ v: 90 }, { v: 95 }, { v: 85 }];

    const ranks = computeRanks(items, i => i.v);

    expect(ranks.get(items[1])).toBe(1);
    expect(ranks.get(items[0])).toBe(2);
    expect(ranks.get(items[2])).toBe(3);
  });

  it('ranks lower values as 1st when lowerIsBetter is true', () => {
    const items = [{ v: 0.5 }, { v: 0 }, { v: 0.2 }];

    const ranks = computeRanks(items, i => i.v, true);

    expect(ranks.get(items[1])).toBe(1);
    expect(ranks.get(items[2])).toBe(2);
    expect(ranks.get(items[0])).toBe(3);
  });

  it('handles a single item', () => {
    const items = [{ v: 42 }];

    const ranks = computeRanks(items, i => i.v);

    expect(ranks.get(items[0])).toBe(1);
  });
});
