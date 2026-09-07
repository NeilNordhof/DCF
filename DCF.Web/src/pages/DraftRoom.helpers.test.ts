import { describe, it, expect } from 'vitest';
import {
  getPickSecondsRemaining, formatPickTimer, isPickTimerLow,
  usesDraftBudget, getEmptyCaptions, shouldWarnEmptyCaption,
} from './DraftRoom.helpers';
import type { ComputedCaption, DraftPick, DraftState, League } from '../types/api';

function pick(userId: string, caption: ComputedCaption): DraftPick {
  return {
    userId,
    displayName: userId,
    corpsId: `${userId}-${caption}`,
    corpsName: 'Corps',
    caption,
    pickNumber: 0,
    roundNumber: 0,
  };
}

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

describe('usesDraftBudget', () => {
  it('prefers the live draft-state flag when present', () => {
    expect(usesDraftBudget({ usesDraftBudget: true, draftBudget: 5 }, { draftBudget: 0 })).toBe(true);
    expect(usesDraftBudget({ usesDraftBudget: false, draftBudget: 0 }, { draftBudget: 6 })).toBe(false);
  });

  it('falls back to the draft-state budget value', () => {
    expect(usesDraftBudget({ usesDraftBudget: undefined, draftBudget: 4 }, { draftBudget: 0 })).toBe(true);
    expect(usesDraftBudget({ usesDraftBudget: undefined, draftBudget: 0 }, { draftBudget: 0 })).toBe(false);
  });

  it('falls back to the league record when draft-state is unavailable', () => {
    expect(usesDraftBudget(null, { draftBudget: 3 })).toBe(true);
    expect(usesDraftBudget(null, { draftBudget: 0 })).toBe(false);
    expect(usesDraftBudget(null, null)).toBe(false);
  });
});

describe('getEmptyCaptions', () => {
  const captions: ComputedCaption[] = ['Brass', 'Percussion', 'Colorguard'];

  it('returns captions the member has not drafted any corps for', () => {
    const picks = [pick('me', 'Brass')];
    expect(getEmptyCaptions(captions, picks, 'me')).toEqual(['Percussion', 'Colorguard']);
  });

  it('returns an empty list when every caption is filled', () => {
    const picks = captions.map(c => pick('me', c));
    expect(getEmptyCaptions(captions, picks, 'me')).toEqual([]);
  });

  it('ignores picks made by other members', () => {
    const picks = [pick('other', 'Brass'), pick('other', 'Percussion')];
    expect(getEmptyCaptions(captions, picks, 'me')).toEqual(captions);
  });

  it('returns nothing when the user is unknown', () => {
    expect(getEmptyCaptions(captions, [], undefined)).toEqual([]);
  });
});

describe('shouldWarnEmptyCaption', () => {
  const base = {
    usesDraftBudget: true,
    status: 'InProgress' as DraftState['status'],
    myPickCount: 1,
    emptyCaptionCount: 1,
  };

  it('warns for a budget league mid-draft with an empty caption after a pick', () => {
    expect(shouldWarnEmptyCaption(base)).toBe(true);
  });

  it('does not warn for corps-per-caption leagues', () => {
    expect(shouldWarnEmptyCaption({ ...base, usesDraftBudget: false })).toBe(false);
  });

  it('does not warn before the draft is in progress', () => {
    expect(shouldWarnEmptyCaption({ ...base, status: 'Open' })).toBe(false);
    expect(shouldWarnEmptyCaption({ ...base, status: 'Completed' })).toBe(false);
  });

  it('does not warn before the member has made any picks', () => {
    expect(shouldWarnEmptyCaption({ ...base, myPickCount: 0 })).toBe(false);
  });

  it('does not warn when there are no empty captions', () => {
    expect(shouldWarnEmptyCaption({ ...base, emptyCaptionCount: 0 })).toBe(false);
  });
});

// League typing sanity: ensure the helpers accept the real DraftState/League shapes.
const _draftStateShape: Partial<DraftState> = { usesDraftBudget: true, draftBudget: 5 };
const _leagueShape: Partial<League> = { draftBudget: 5 };
void _draftStateShape;
void _leagueShape;
