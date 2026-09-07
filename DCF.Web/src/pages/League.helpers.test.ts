import { describe, it, expect } from 'vitest';
import { PICK_TIMER_OPTIONS, pickTimerLabel } from './League.helpers';

describe('PICK_TIMER_OPTIONS', () => {
  it('starts with the "No limit" option at value 0', () => {
    expect(PICK_TIMER_OPTIONS[0]).toEqual({ value: 0, label: 'No limit' });
  });
});

describe('pickTimerLabel', () => {
  it('labels undefined and zero as "No limit"', () => {
    expect(pickTimerLabel(undefined)).toBe('No limit');
    expect(pickTimerLabel(0)).toBe('No limit');
  });

  it('uses the preset label for known option values', () => {
    expect(pickTimerLabel(30)).toBe('30 seconds');
    expect(pickTimerLabel(180)).toBe('3 minutes');
  });

  it('falls back to a formatted label for values outside the preset list', () => {
    expect(pickTimerLabel(45)).toBe('45 seconds');
    expect(pickTimerLabel(240)).toBe('4 minutes');
  });
});
