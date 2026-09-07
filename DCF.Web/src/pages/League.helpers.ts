export const PICK_TIMER_OPTIONS: { value: number; label: string }[] = [
  { value: 0, label: 'No limit' },
  { value: 30, label: '30 seconds' },
  { value: 60, label: '1 minute' },
  { value: 90, label: '90 seconds' },
  { value: 120, label: '2 minutes' },
  { value: 180, label: '3 minutes' },
  { value: 300, label: '5 minutes' },
];

export function pickTimerLabel(seconds: number | undefined): string {
  if (!seconds) return 'No limit';
  const match = PICK_TIMER_OPTIONS.find(o => o.value === seconds);
  if (match) return match.label;
  return seconds % 60 === 0 ? `${seconds / 60} minute${seconds === 60 ? '' : 's'}` : `${seconds} seconds`;
}
