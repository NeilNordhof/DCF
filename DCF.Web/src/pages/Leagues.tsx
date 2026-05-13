import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { League } from '../types/api';

export function Leagues() {
  const [leagues, setLeagues] = useState<League[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.getLeagues().then(setLeagues).catch(() => setError('Failed to load leagues.'));
  }, []);

  if (error) return <div>{error}</div>;

  return (
    <div>
      <h2>Leagues</h2>
      <Link to="/leagues/create">+ Create League</Link>
      <ul>
        {leagues.map(l => (
          <li key={l.id}>
            <Link to={`/leagues/${l.id}`}>{l.name}</Link>
            {' '}— {l.seasonYear} — {l.draftStatus}
            {l.isMember && ' ✓'}
          </li>
        ))}
      </ul>
    </div>
  );
}
