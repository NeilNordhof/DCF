import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import type { DciStandingsEntry } from '../types/api';
import { DciScheduleTab } from './DciScheduleTab';

type SortColumn = 'latest' | 'last3Avg';

function StandingsTab({ seasonId }: { seasonId: string }) {
  const [entries, setEntries] = useState<DciStandingsEntry[] | null>(null);
  const [sortColumn, setSortColumn] = useState<SortColumn>('latest');
  const [sortDesc, setSortDesc] = useState(true);

  useEffect(() => {
    api.getDciStandings(seasonId).then(setEntries);
  }, [seasonId]);

  if (entries === null) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>Loading standings...</p>;
  }

  function toggleSort(column: SortColumn) {
    if (sortColumn === column) {
      setSortDesc(d => !d);
    } else {
      setSortColumn(column);
      setSortDesc(true);
    }
  }

  const sorted = [...entries].sort((a, b) => {
    const av = sortColumn === 'latest' ? a.latest.score : a.last3Avg;
    const bv = sortColumn === 'latest' ? b.latest.score : b.last3Avg;
    return sortDesc ? bv - av : av - bv;
  });

  const arrow = (column: SortColumn) => (sortColumn === column ? (sortDesc ? '▼' : '▲') : '');

  return (
    <div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, padding: 14 }}>
      <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 11 }}>
        <thead>
          <tr>
            <th style={{ width: 28, textAlign: 'left', fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', padding: '8px 10px', borderBottom: '1px solid var(--border)' }}>#</th>
            <th style={{ textAlign: 'left', fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', padding: '8px 10px', borderBottom: '1px solid var(--border)' }}>Corps</th>
            <th
              onClick={() => toggleSort('latest')}
              style={{ textAlign: 'left', fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', padding: '8px 10px', borderBottom: '1px solid var(--border)', cursor: 'pointer' }}
            >
              Latest Score <span style={{ color: 'var(--accent)' }}>{arrow('latest')}</span>
            </th>
            <th
              onClick={() => toggleSort('last3Avg')}
              title="Hover a score below to see the 3 shows it's averaged from"
              style={{ textAlign: 'left', fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', padding: '8px 10px', borderBottom: '1px solid var(--border)', cursor: 'pointer' }}
            >
              Last 3 Avg <span style={{ color: 'var(--accent)' }}>{arrow('last3Avg')}</span>
            </th>
            <th style={{ textAlign: 'left', fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', padding: '8px 10px', borderBottom: '1px solid var(--border)' }}>Last Event</th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((entry, i) => (
            <tr key={entry.corpsId}>
              <td style={{ padding: '9px 10px', color: 'var(--text-heading)', fontWeight: 700, borderBottom: '1px solid var(--border)' }}>{i + 1}</td>
              <td style={{ padding: '9px 10px', color: 'var(--text-heading)', fontWeight: 600, borderBottom: '1px solid var(--border)' }}>{entry.corpsName}</td>
              <td style={{ padding: '9px 10px', color: 'var(--text-heading)', fontWeight: 700, borderBottom: '1px solid var(--border)' }}>{entry.latest.score.toFixed(3)}</td>
              <td style={{ padding: '9px 10px', borderBottom: '1px solid var(--border)' }}>
                <span
                  title={entry.last3.map(s => `${s.score.toFixed(3)} – ${s.showName} – ${s.date}`).join('\n')}
                  style={{ color: 'var(--text-heading)', fontWeight: 700, borderBottom: '1px dotted var(--text-faint)', cursor: 'help' }}
                >
                  {entry.last3Avg.toFixed(3)}
                </span>
              </td>
              <td style={{ padding: '9px 10px', color: 'var(--text-faint)', borderBottom: '1px solid var(--border)' }}>{entry.latest.showName} · {entry.latest.date}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function Dci() {
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = searchParams.get('tab') === 'schedule' ? 'schedule' : searchParams.get('tab') === 'scores' ? 'scores' : 'standings';
  const [seasonId, setSeasonId] = useState<string | null>(null);
  const [seasonLoadFailed, setSeasonLoadFailed] = useState(false);

  useEffect(() => {
    api.getDciCurrentSeason().then(season => setSeasonId(season.id)).catch(() => setSeasonLoadFailed(true));
  }, []);

  function setTab(t: 'standings' | 'schedule' | 'scores') {
    if (t === 'standings') {
      setSearchParams({});
    } else {
      setSearchParams({ tab: t });
    }
  }

  const tabStyle = (active: boolean): React.CSSProperties => ({
    padding: '8px 16px',
    fontSize: 11,
    fontWeight: active ? 700 : 600,
    color: active ? 'var(--accent)' : 'var(--text-muted)',
    borderBottom: active ? '2px solid var(--accent)' : '2px solid transparent',
    letterSpacing: '0.5px',
    textTransform: 'uppercase',
    background: 'none',
    border: 'none',
    borderBottomWidth: 2,
    borderBottomStyle: 'solid',
    cursor: 'pointer',
  });

  if (seasonLoadFailed) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>No current season data available.</p>;
  }

  if (seasonId === null) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>Loading...</p>;
  }

  return (
    <div>
      <div style={{ display: 'flex', gap: 4, marginBottom: 14 }}>
        <button style={tabStyle(tab === 'standings')} onClick={() => setTab('standings')}>Standings</button>
        <button style={tabStyle(tab === 'schedule')} onClick={() => setTab('schedule')}>Schedule</button>
        <button style={tabStyle(tab === 'scores')} onClick={() => setTab('scores')}>Scores</button>
      </div>
      {tab === 'standings' && <StandingsTab seasonId={seasonId} />}
      {tab === 'schedule' && <DciScheduleTab seasonId={seasonId} />}
      {tab === 'scores' && <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>Scores tab coming in Task 10.</p>}
      <p style={{ marginTop: 20 }}><Link to="/leagues" style={{ color: 'var(--text-faint)', fontSize: 10 }}>← Back to Leagues</Link></p>
    </div>
  );
}
