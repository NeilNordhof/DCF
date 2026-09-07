import type { ComputedCaption, DraftPick, DraftState, League } from '../types/api';

export function getPickSecondsRemaining(pickDeadline: string | null | undefined, now: number): number | null {
  if (!pickDeadline) return null;
  const diff = new Date(pickDeadline).getTime() - now;
  return Math.max(0, Math.ceil(diff / 1000));
}

export function formatPickTimer(secondsRemaining: number): string {
  const m = Math.floor(secondsRemaining / 60);
  const s = secondsRemaining % 60;
  return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`;
}

export function isPickTimerLow(secondsRemaining: number | null, thresholdSeconds = 10): boolean {
  return secondsRemaining !== null && secondsRemaining <= thresholdSeconds;
}

/**
 * Whether a league is configured with the `Draft Budget` allocation model,
 * where members spend a pool of corps-caption picks across captions however
 * they like, rather than drafting a fixed number of corps per caption.
 *
 * Prefers the live draft-state value (published over MQTT) and falls back to
 * the league record loaded over HTTP.
 */
export function usesDraftBudget(
  draftState: Pick<DraftState, 'usesDraftBudget' | 'draftBudget'> | null | undefined,
  league: Pick<League, 'draftBudget'> | null | undefined,
): boolean {
  if (draftState?.usesDraftBudget != null) {
    return draftState.usesDraftBudget;
  }

  return (draftState?.draftBudget ?? league?.draftBudget ?? 0) > 0;
}

/**
 * Returns the draftable captions for which the given member has drafted no
 * corps. Only meaningful for `Draft Budget` leagues, where empty captions are
 * allowed (unlike `Corps Per Caption` leagues, which require every caption to
 * be filled).
 */
export function getEmptyCaptions(
  captions: ComputedCaption[] | undefined,
  picks: DraftPick[] | undefined,
  userId: string | undefined,
): ComputedCaption[] {
  if (!captions || !userId) {
    return [];
  }

  return captions.filter(
    cap => !(picks ?? []).some(p => p.userId === userId && p.caption === cap),
  );
}

/**
 * Whether the draft UI should warn a member that they have one or more empty
 * captions. This only applies to `Draft Budget` leagues while a draft is in
 * progress and after the member has made at least one pick (so we do not nag
 * before they have started allocating their budget).
 */
export function shouldWarnEmptyCaption(args: {
  usesDraftBudget: boolean;
  status: DraftState['status'];
  myPickCount: number;
  emptyCaptionCount: number;
}): boolean {
  return (
    args.usesDraftBudget &&
    args.status === 'InProgress' &&
    args.myPickCount > 0 &&
    args.emptyCaptionCount > 0
  );
}
