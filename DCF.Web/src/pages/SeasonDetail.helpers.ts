import type { Show, TriggerScrapeResult } from '../types/api';

export const TZ_HOURS: Record<string, number> = { PT: 7, MT: 6, CT: 5, ET: 4 };

export function buildDateTime(date: string, time: string, tz: string): string {
  const d = new Date(`${date}T${time}:00Z`);
  d.setUTCHours(d.getUTCHours() + (TZ_HOURS[tz] ?? 4));
  return d.toISOString();
}

export function buildScheduleEntryTime(date: string, time: string | null, tz: string): string | null {
  return time ? buildDateTime(date, time, tz) : null;
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

export interface ScrapeResultMessage {
  text: string;
  color: string;
  sticky: boolean;
}

export function getScrapeResultMessage(result: TriggerScrapeResult): ScrapeResultMessage {
  if (result.outcome === 'Succeeded') {
    return { text: '✓ Scrape succeeded', color: 'var(--green)', sticky: false };
  }

  if (result.outcome === 'Failed') {
    return { text: `✗ Scrape failed: ${result.error ?? 'Unknown error'}`, color: 'var(--red)', sticky: true };
  }

  return { text: 'Scrape skipped', color: 'var(--accent)', sticky: false };
}

export interface SchedulePayloadEntry {
  time: string | null;
  label: string;
  corpsId: string | null;
}

export function buildSchedulePayload(
  entries: SchedulePayloadEntry[],
  baseDate: string,
  tz: string
): SchedulePayloadEntry[] {
  let rolloverDate = baseDate;
  let prevTime = '';

  return entries.map(entry => {
    if (entry.time && prevTime && entry.time < prevTime && prevTime >= '12:00') {
      const d = new Date(`${rolloverDate}T00:00:00`);
      d.setDate(d.getDate() + 1);
      rolloverDate = d.toISOString().slice(0, 10);
    }

    if (entry.time) {
      prevTime = entry.time;
    }

    return {
      time: buildScheduleEntryTime(rolloverDate, entry.time, tz),
      label: entry.label,
      corpsId: entry.corpsId,
    };
  });
}

export type ShowFilterBucket = 'upcoming' | 'needsAttention' | 'done';

export function getShowFilterBucket(show: Show): ShowFilterBucket {
  if (!show.isExhibition && show.scrapeStatus === 'Failed' && !show.noScoreReason) {
    return 'needsAttention';
  }

  if (show.noScoreReason || show.scrapeStatus === 'Succeeded' || (show.isExhibition && hasScoresAnnounced(show))) {
    return 'done';
  }

  return 'upcoming';
}
