import { describe, it, expect } from 'vitest';
import { formatShowDate, groupByWeek, tabStyle } from './Dci.helpers';

interface Item {
  date: string;
}

describe('groupByWeek', () => {
  it('groups items into Sunday-starting week buckets, ordered ascending by week', () => {
    const items: Item[] = [
      { date: '2026-07-27' },
      { date: '2026-07-29' },
      { date: '2026-08-03' },
    ];

    const groups = groupByWeek(items, i => i.date);

    expect(groups).toHaveLength(2);
    expect(groups[0].items).toHaveLength(2);
    expect(groups[1].items).toHaveLength(1);
  });

  it('labels each week as "Week of <Month> <Day>" using the week-start date', () => {
    const items: Item[] = [{ date: '2026-07-28' }];

    const groups = groupByWeek(items, i => i.date);

    expect(groups[0].weekLabel).toBe('Week of Jul 26');
  });

  it('returns an empty array for no items', () => {
    expect(groupByWeek<Item>([], i => i.date)).toEqual([]);
  });
});

describe('formatShowDate', () => {
  it('formats an ISO date string as "<Weekday>, <Month> <Day>" in UTC', () => {
    expect(formatShowDate('2026-07-15')).toBe('Wed, Jul 15');
  });
});

describe('tabStyle', () => {
  // Applies the style object to a real DOM node the same way React does (sequential
  // per-key assignment onto CSSStyleDeclaration), so shorthand/longhand ordering bugs
  // in the object itself are actually exercised, not just its shape.
  function applyToButton(style: ReturnType<typeof tabStyle>): HTMLButtonElement {
    const button = document.createElement('button');

    for (const [key, value] of Object.entries(style)) {
      const cssValue = typeof value === 'number' ? `${value}px` : String(value);

      Reflect.set(button.style, key, cssValue);
    }

    return button;
  }

  it('gives the active tab a solid accent-colored underline', () => {
    const button = applyToButton(tabStyle(true));

    // jsdom's CSSOM can't decompose a var()-valued shorthand into borderBottomColor,
    // so this reads the shorthand's own stored value rather than the longhand getter.
    expect(button.style.borderBottom).toBe('2px solid var(--accent)');
  });

  it('gives an inactive tab a fully transparent underline, not a visible muted-color one', () => {
    const button = applyToButton(tabStyle(false));

    expect(button.style.borderBottom).toBe('2px solid transparent');
    expect(button.style.borderBottomColor).toBe('transparent');
  });
});
