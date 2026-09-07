import { describe, it, expect } from 'vitest';
import { getPickSecondsRemaining, formatPickTimer, isPickTimerLow } from './DraftRoom.helpers';

describe('getPickSecondsRemaining', () => {
  it('returns null when there is no pick deadline', () => {
    expect(getPickSecondsRemaining(undefined, Date.now())).toBeNull();
    expect(getPickSecondsRemaining(null, Date.now())).toBeNull();
  });

  it('rounds up the remaining seconds until the deadline', () => {
    const now = new Date('2026-01-01T00:00:00.000Z').getTime();
    const deadline = new Date('2026-01-01T00:00:30.400Z').toISOString();

    expect(getPickSecondsRemaining(deadline, now)).toBe(31);
  });

  it('clamps to zero once the deadline has passed', () => {
    const now = new Date('2026-01-01T00:01:00.000Z').getTime();
    const deadline = new Date('2026-01-01T00:00:30.000Z').toISOString();

    expect(getPickSecondsRemaining(deadline, now)).toBe(0);
  });
});

describe('formatPickTimer', () => {
  it('formats seconds as MM:SS', () => {
    expect(formatPickTimer(5)).toBe('00:05');
    expect(formatPickTimer(65)).toBe('01:05');
    expect(formatPickTimer(600)).toBe('10:00');
  });

  it('formats zero seconds as 00:00', () => {
    expect(formatPickTimer(0)).toBe('00:00');
  });
});

describe('isPickTimerLow', () => {
  it('returns false when there is no timer running', () => {
    expect(isPickTimerLow(null)).toBe(false);
  });

  it('returns true at or below the default 10 second threshold', () => {
    expect(isPickTimerLow(10)).toBe(true);
    expect(isPickTimerLow(0)).toBe(true);
  });

  it('returns false above the threshold', () => {
    expect(isPickTimerLow(11)).toBe(false);
  });

  it('respects a custom threshold', () => {
    expect(isPickTimerLow(20, 30)).toBe(true);
    expect(isPickTimerLow(31, 30)).toBe(false);
  });
});
