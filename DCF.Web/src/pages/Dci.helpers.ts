import type { CSSProperties } from 'react';

export interface WeekGroup<T> {
  weekLabel: string;
  items: T[];
}

export function formatShowDate(dateStr: string): string {
  return new Date(`${dateStr}T00:00:00Z`).toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', timeZone: 'UTC' });
}

export function tabStyle(active: boolean): CSSProperties {
  return {
    padding: '8px 16px',
    fontSize: 11,
    fontWeight: active ? 700 : 600,
    color: active ? 'var(--accent)' : 'var(--text-muted)',
    letterSpacing: '0.5px',
    textTransform: 'uppercase',
    background: 'none',
    border: 'none',
    borderBottom: active ? '2px solid var(--accent)' : '2px solid transparent',
    cursor: 'pointer',
  };
}

export function groupByWeek<T>(items: T[], getDate: (item: T) => string): WeekGroup<T>[] {
  const groups = new Map<string, T[]>();

  for (const item of items) {
    const date = new Date(`${getDate(item)}T00:00:00Z`);
    const daysFromSunday = date.getUTCDay();
    const weekStart = new Date(date);
    weekStart.setUTCDate(date.getUTCDate() - daysFromSunday);

    const key = weekStart.toISOString().slice(0, 10);
    const existing = groups.get(key) ?? [];
    existing.push(item);
    groups.set(key, existing);
  }

  return Array.from(groups.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([key, groupItems]) => {
      const weekStart = new Date(`${key}T00:00:00Z`);
      const weekLabel = `Week of ${weekStart.toLocaleDateString('en-US', { month: 'short', day: 'numeric', timeZone: 'UTC' })}`;

      return { weekLabel, items: groupItems };
    });
}
