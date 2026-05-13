import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { League } from '../types/api';

export function Leagues() {
  const [leagues, setLeagues] = useState<League[]>([]);

  useEffect(() => { api.getLeagues().then(setLeagues); }, []);

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
