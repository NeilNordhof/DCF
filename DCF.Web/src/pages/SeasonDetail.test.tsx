import { describe, it, expect } from 'vitest';
import { buildDateTime, buildScheduleEntryTime, toNullableIso } from './SeasonDetail';

describe('buildDateTime', () => {
  it('composes a date, HH:MM time, and timezone into a UTC ISO string', () => {
    expect(buildDateTime('2026-08-15', '19:00', 'ET')).toBe('2026-08-15T23:00:00.000Z');
  });
});

describe('buildScheduleEntryTime', () => {
  it('builds an ISO datetime when a time is present', () => {
    expect(buildScheduleEntryTime('2026-08-15', '19:00', 'ET')).toBe('2026-08-15T23:00:00.000Z');
  });

  it('returns null for an unscheduled (TBD) entry instead of throwing', () => {
    expect(buildScheduleEntryTime('2026-08-15', null, 'ET')).toBeNull();
  });
});

describe('toNullableIso', () => {
  it('converts an existing ISO time string to ISO', () => {
    expect(toNullableIso('2026-08-15T23:00:00.000Z')).toBe('2026-08-15T23:00:00.000Z');
  });

  it('returns null for an unscheduled (TBD) entry instead of the Unix epoch', () => {
    expect(toNullableIso(null)).toBeNull();
  });
});
