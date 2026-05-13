import { useAuth0 } from '@auth0/auth0-react';
import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useMqtt } from '../mqtt/useMqtt';
import type { League, Standing } from '../types/api';

export function LeagueDetail() {
  const { id } = useParams<{ id: string }>();
  const { user: _user } = useAuth0();
  const [league, setLeague] = useState<League | null>(null);
  const [standings, setStandings] = useState<Standing[]>([]);
  useMqtt(`dcf/leagues/${id}/draft`);
  const scoresUpdated = useMqtt<{ showId: string }>('dcf/scores/updated');

  useEffect(() => {
    if (id) api.getLeague(id).then(setLeague);
  }, [id]);

  useEffect(() => {
    if (id) api.getStandings(id).then(setStandings);
  }, [id, scoresUpdated]);

  if (!league) return <div>Loading...</div>;

  const joinLeague = async () => {
    const code = league.isPublic ? undefined : prompt('Enter invite code:') ?? undefined;
    await api.joinLeague(league.id, code);
    window.location.reload();
  };

  return (
    <div>
      <h2>{league.name}</h2>
      <p>Season: {league.seasonYear} | Status: {league.draftStatus}</p>
      {league.inviteCode && <p>Invite code: <code>{league.inviteCode}</code></p>}

      <Link to={`/leagues/${id}/draft`}>Draft Room</Link>
      {!league.isMember && <button onClick={joinLeague}>Join League</button>}

      <h3>Standings</h3>
      <ol>
        {standings.map(s => (
          <li key={s.userId}>{s.displayName} — {s.score.toFixed(3)}</li>
        ))}
      </ol>

      <h3>Members ({league.members?.length ?? 0})</h3>
      <ul>
        {league.members?.map(m => <li key={m.userId}>{m.displayName}</li>)}
      </ul>
    </div>
  );
}
