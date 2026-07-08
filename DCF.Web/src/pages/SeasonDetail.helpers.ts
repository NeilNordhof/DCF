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
