import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { DciScheduleShow } from '../types/api';
import { groupByWeek } from './Dci.helpers';
import { WeekRow } from './DciWeekRow';

function ShowCard({ show }: { show: DciScheduleShow }) {
  const dateLabel = new Date(`${show.date}T00:00:00Z`).toLocaleDateString('en-US', { weekday: 'short', month: 'short', day: 'numeric', timeZone: 'UTC' });
  const timeLabel = show.startTime
    ? new Date(show.startTime).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })
    : null;

  return (
    <div style={{ flex: '0 0 230px', width: 230, background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, padding: 12 }}>
      <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--text-heading)', lineHeight: 1.3 }}>{show.name}</div>
      {show.isExhibition && (
        <span style={{ display: 'inline-block', fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.4px', color: 'var(--text-muted)', background: 'var(--surface-2)', border: '1px solid var(--border)', borderRadius: 3, padding: '2px 6px', marginTop: 5 }}>
          Exhibition
        </span>
      )}
      <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 5 }}>
        {dateLabel}{timeLabel ? ` · ${timeLabel}${show.timezone ? ` ${show.timezone}` : ''}` : ''}
      </div>
      {show.location && <div style={{ fontSize: 11, color: 'var(--text-faint)', marginTop: 2 }}>{show.location}</div>}
      {show.schedule.length > 0 && (
        <div style={{ marginTop: 10, paddingTop: 10, borderTop: '1px solid var(--border)' }}>
          {show.schedule.map((entry, i) => (
            <div key={i} style={{ display: 'flex', gap: 8, fontSize: 11.5, padding: '2.5px 0' }}>
              <span style={{ flex: '0 0 44px', whiteSpace: 'nowrap', color: entry.time ? 'var(--text-muted)' : 'var(--text-faint)', fontStyle: entry.time ? 'normal' : 'italic' }}>
                {entry.time ? new Date(entry.time).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' }) : 'TBD'}
              </span>
              <span style={{ flex: 1, textAlign: 'left', color: 'var(--text)' }}>{entry.corpsName ?? entry.label}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function DciScheduleTab({ seasonId }: { seasonId: string }) {
  const [shows, setShows] = useState<DciScheduleShow[] | null>(null);

  useEffect(() => {
    api.getDciSchedule(seasonId).then(setShows);
  }, [seasonId]);

  if (shows === null) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>Loading schedule...</p>;
  }

  const weeks = groupByWeek(shows, s => s.date);

  if (weeks.length === 0) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>No upcoming shows scheduled.</p>;
  }

  return (
    <div>
      {weeks.map(week => (
        <WeekRow key={week.weekLabel} weekLabel={week.weekLabel} items={week.items} renderCard={show => <ShowCard key={show.id} show={show} />} />
      ))}
    </div>
  );
}
