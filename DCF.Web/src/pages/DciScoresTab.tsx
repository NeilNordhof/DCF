import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { DciScoresShow } from '../types/api';
import { formatShowDate, groupByWeek } from './Dci.helpers';
import { WeekRow } from './DciWeekRow';

function ScoreCard({ show }: { show: DciScoresShow }) {
  const dateLabel = formatShowDate(show.date);

  return (
    <div style={{ flex: '0 0 230px', width: 230, background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, padding: 12 }}>
      <div style={{ fontSize: 14, fontWeight: 700, color: 'var(--text-heading)', lineHeight: 1.3 }}>{show.name}</div>
      {show.isExhibition && (
        <span style={{ display: 'inline-block', fontSize: 10, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.4px', color: 'var(--text-muted)', background: 'var(--surface-2)', border: '1px solid var(--border)', borderRadius: 3, padding: '2px 6px', marginTop: 5 }}>
          Exhibition
        </span>
      )}
      <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 5 }}>{dateLabel}</div>

      {show.noScoreReason ? (
        <div style={{ marginTop: 10, paddingTop: 10, borderTop: '1px solid var(--border)', fontSize: 11, color: 'var(--text-faint)', fontStyle: 'italic' }}>
          No score: {show.noScoreReason}
        </div>
      ) : show.scoresPending ? (
        <div style={{ marginTop: 10, paddingTop: 10, borderTop: '1px solid var(--border)', fontSize: 11, color: 'var(--text-faint)', fontStyle: 'italic' }}>
          Scores pending
        </div>
      ) : show.results.length > 0 ? (
        <>
          <div style={{ marginTop: 10, paddingTop: 10, borderTop: '1px solid var(--border)' }}>
            {show.results.map(result => (
              <div key={result.corpsId} style={{ display: 'flex', gap: 8, fontSize: 11.5, padding: '2.5px 0' }}>
                <span style={{ flex: '0 0 14px', color: 'var(--text-muted)', fontWeight: 700 }}>{result.rank}</span>
                <span style={{ flex: 1, textAlign: 'left', color: 'var(--text)' }}>{result.corpsName}</span>
                <span style={{ flex: '0 0 44px', textAlign: 'right', color: 'var(--text-heading)', fontWeight: 700 }}>{result.totalScore.toFixed(3)}</span>
              </div>
            ))}
          </div>
          <Link
            to={`/dci/recap/${show.id}`}
            style={{ display: 'block', marginTop: 10, paddingTop: 10, borderTop: '1px solid var(--border)', fontSize: 11, fontWeight: 700, color: 'var(--accent)', textDecoration: 'none', textAlign: 'center' }}
          >
            View Recap →
          </Link>
        </>
      ) : null}
    </div>
  );
}

export function DciScoresTab({ seasonId }: { seasonId: string }) {
  const [shows, setShows] = useState<DciScoresShow[] | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);

  useEffect(() => {
    api.getDciScores(seasonId).then(setShows).catch(() => setLoadFailed(true));
  }, [seasonId]);

  if (loadFailed) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>Failed to load scores.</p>;
  }

  if (shows === null) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>Loading scores...</p>;
  }

  const weeks = groupByWeek(shows, s => s.date).reverse();

  if (weeks.length === 0) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>No completed shows yet this season.</p>;
  }

  return (
    <div>
      {weeks.map(week => (
        <WeekRow key={week.weekLabel} weekLabel={week.weekLabel} items={week.items} renderCard={show => <ScoreCard key={show.id} show={show} />} />
      ))}
    </div>
  );
}
