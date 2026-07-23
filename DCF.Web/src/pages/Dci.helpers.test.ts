import { describe, it, expect } from 'vitest';
import { formatShowDate, groupByWeek } from './Dci.helpers';

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
