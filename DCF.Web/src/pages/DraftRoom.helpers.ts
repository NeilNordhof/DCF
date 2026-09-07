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
