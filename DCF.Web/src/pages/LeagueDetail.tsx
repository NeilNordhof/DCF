import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useMqtt } from '../mqtt/useMqtt';
import { useUser } from '../context/UserContext';
import type { DraftState, League, Standing } from '../types/api';

export function LeagueDetail() {
  const { id } = useParams<{ id: string }>();
  const { user } = useUser();
  const [league, setLeague] = useState<League | null>(null);
  const [standings, setStandings] = useState<Standing[]>([]);
  const [error, setError] = useState<string | null>(null);
  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);
  const scoresUpdated = useMqtt<{ showId: string }>('dcf/scores/updated');

  useEffect(() => {
    if (id) api.getLeague(id).then(setLeague).catch(() => setError('Failed to load league.'));
  }, [id]);

  useEffect(() => {
    if (id) api.getStandings(id).then(setStandings).catch(() => {});
  }, [id, scoresUpdated]);

  if (error) return <div>{error}</div>;
  if (!league) return <div>Loading...</div>;

  const isCommissioner = user?.id !== undefined && user.id === league.commissionerUserId;
  const isDraftRoomOpen = draftState?.status === 'Open'
    || draftState?.status === 'InProgress'
    || draftState?.status === 'Completed';

  const joinLeague = async () => {
    const code = league.isPublic ? undefined : prompt('Enter invite code:') ?? undefined;
    await api.joinLeague(league.id, code);
    window.location.reload();
  };

  const openDraft = () => id && api.openDraft(id).catch(() => {});

  return (
    <div>
      <h2>{league.name}</h2>
      <p>Season: {league.seasonYear} | Status: {league.draftStatus}</p>
      {league.inviteCode && <p>Invite code: <code>{league.inviteCode}</code></p>}

      {isDraftRoomOpen
        ? <Link to={`/leagues/${id}/draft`}>Join Draft Room</Link>
        : <span>Draft Room not open yet</span>}

      {isCommissioner && league.draftStatus === 'NotStarted' && !draftState && (
        <button onClick={openDraft}>Open Draft</button>
      )}

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
