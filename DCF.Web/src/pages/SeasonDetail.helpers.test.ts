import { describe, it, expect } from 'vitest';
import { buildDateTime, buildScheduleEntryTime, toNullableIso, getShowStatusBadge } from './SeasonDetail.helpers';
import type { Show } from '../types/api';

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

function makeShow(overrides: Partial<Show> = {}): Show {
  return {
    id: 'show-1',
    name: 'Test Show',
    date: '2026-08-15',
    isExhibition: false,
    corpsIds: [],
    scrapeStatus: 'NotStarted',
    schedule: [],
    noScoreReason: null,
    ...overrides,
  };
}

describe('getShowStatusBadge', () => {
  const past = new Date(Date.now() - 60 * 60 * 1000).toISOString();
  const future = new Date(Date.now() + 60 * 60 * 1000).toISOString();

  it('returns a NO SCORES badge when noScoreReason is set, regardless of other state', () => {
    const show = makeShow({ noScoreReason: 'Storm forced standstill exhibition', startTime: past, scoresAnnouncedTime: past });
    expect(getShowStatusBadge(show)).toEqual({ label: 'NO SCORES', color: 'var(--red)' });
  });

  it('returns a COMPLETED badge for an exhibition show whose concludes time has passed', () => {
    const show = makeShow({ isExhibition: true, scoresAnnouncedTime: past });
    expect(getShowStatusBadge(show)).toEqual({ label: 'COMPLETED', color: 'var(--green)' });
  });

  it('does not return COMPLETED for an exhibition show whose concludes time has not passed', () => {
    const show = makeShow({ isExhibition: true, scoresAnnouncedTime: future });
    expect(getShowStatusBadge(show)).toBeNull();
  });

  it('returns a SCORES ANNOUNCED badge for a competitive show once scores time has passed', () => {
    const show = makeShow({ scoresAnnouncedTime: past });
    expect(getShowStatusBadge(show)).toEqual({ label: 'SCORES ANNOUNCED', color: 'var(--green)' });
  });

  it('returns a STARTED badge once start time has passed but scores have not been announced', () => {
    const show = makeShow({ startTime: past, scoresAnnouncedTime: future });
    expect(getShowStatusBadge(show)).toEqual({ label: 'STARTED', color: 'var(--accent)' });
  });

  it('returns null for a show that has not started', () => {
    const show = makeShow({ startTime: future, scoresAnnouncedTime: future });
    expect(getShowStatusBadge(show)).toBeNull();
  });
});
