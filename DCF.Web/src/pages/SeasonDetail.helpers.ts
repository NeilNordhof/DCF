import type { Show } from '../types/api';

export const TZ_HOURS: Record<string, number> = { PT: 7, MT: 6, CT: 5, ET: 4 };

export function buildDateTime(date: string, time: string, tz: string): string {
  const d = new Date(`${date}T${time}:00Z`);
  d.setUTCHours(d.getUTCHours() + (TZ_HOURS[tz] ?? 4));
  return d.toISOString();
}

export function buildScheduleEntryTime(date: string, time: string | null, tz: string): string | null {
  return time ? buildDateTime(date, time, tz) : null;
}

export function toNullableIso(time: string | null): string | null {
  return time ? new Date(time).toISOString() : null;
}

export function hasStarted(show: Show): boolean {
  return !!show.startTime && new Date(show.startTime) <= new Date();
}

export function hasScoresAnnounced(show: Show): boolean {
  return !!show.scoresAnnouncedTime && new Date(show.scoresAnnouncedTime) <= new Date();
}

export interface ShowStatusBadge {
  label: string;
  color: string;
}

export function getShowStatusBadge(show: Show): ShowStatusBadge | null {
  if (show.noScoreReason) {
    return { label: 'NO SCORES', color: 'var(--red)' };
  }

  if (show.isExhibition && hasScoresAnnounced(show)) {
    return { label: 'COMPLETED', color: 'var(--green)' };
  }

  if (hasScoresAnnounced(show)) {
    return { label: 'SCORES ANNOUNCED', color: 'var(--green)' };
  }

  if (hasStarted(show)) {
    return { label: 'STARTED', color: 'var(--accent)' };
  }

  return null;
}
