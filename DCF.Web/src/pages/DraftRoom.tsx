import { useAuth0 } from '@auth0/auth0-react';
import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useMqtt } from '../mqtt/useMqtt';
import type { Corps, DraftState, League } from '../types/api';

export function DraftRoom() {
  const { id } = useParams<{ id: string }>();
  const { user: _user } = useAuth0();
  const [league, setLeague] = useState<League | null>(null);
  const [corps, setCorps] = useState<Corps[]>([]);
  const [selectedCorps, setSelectedCorps] = useState('');
  const [selectedCaption, setSelectedCaption] = useState('');
  const [myProfile, setMyProfile] = useState<{ id: string } | null>(null);

  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);

  useEffect(() => {
    if (!id) return;
    api.getLeague(id).then(setLeague);
    api.adminGetCorps().then(setCorps);
    api.upsertUser().then(p => setMyProfile(p));
  }, [id]);

  if (!league) return <div>Loading...</div>;

  const isMyTurn = draftState?.status === 'InProgress' &&
    draftState.currentDrafterId === myProfile?.id;

  const isCommissioner = myProfile?.id !== undefined &&
    myProfile.id === league.commissionerUserId;

  const takenCombos = new Set(
    (draftState?.picks ?? []).map(p => `${p.corpsId}|${p.caption}`)
  );

  const availableCorps = corps.filter(c =>
    !league.draftableCaptions.every(cap => takenCombos.has(`${c.id}|${cap}`))
  );

  const submitPick = async () => {
    if (!id || !selectedCorps || !selectedCaption) return;
    await api.submitPick(id, selectedCorps, selectedCaption);
    setSelectedCorps('');
    setSelectedCaption('');
  };

  const skipPick = () => id && api.skipPick(id);
  const startDraft = () => id && api.startDraft(id);

  // Lobby view
  if (!draftState || draftState.status === 'NotStarted' || draftState.status === 'Scheduled') {
    return (
      <div>
        <h2>{league.name} — Draft Lobby</h2>
        {league.draftStartTime && (
          <p>Draft starts: {new Date(league.draftStartTime).toLocaleString()}</p>
        )}
        <h3>Members joined:</h3>
        <ul>
          {(draftState?.members ?? league.members ?? []).map(m => (
            <li key={m.userId}>{m.displayName}</li>
          ))}
        </ul>
        {isCommissioner && league.draftStatus === 'NotStarted' && (
          <button onClick={startDraft}>Start Draft Now</button>
        )}
      </div>
    );
  }

  // Completed view
  if (draftState.status === 'Completed') {
    return (
      <div>
        <h2>Draft Complete</h2>
        <ol>
          {draftState.picks.map(p => (
            <li key={p.pickNumber}>
              Pick {p.pickNumber + 1}: {p.displayName} → {p.corpsName} ({p.caption})
            </li>
          ))}
        </ol>
      </div>
    );
  }

  // In-progress draft view
  const currentDrafter = draftState.members.find(
    m => m.userId === draftState.currentDrafterId
  );

  return (
    <div>
      <h2>{league.name} — Live Draft</h2>
      <p>Now picking: <strong>{currentDrafter?.displayName ?? '...'}</strong></p>

      {isMyTurn && (
        <div>
          <h3>Your pick</h3>
          <select value={selectedCorps} onChange={e => setSelectedCorps(e.target.value)}>
            <option value="">Select corps...</option>
            {availableCorps.map(c => (
              <option key={c.id} value={c.id}>{c.name}</option>
            ))}
          </select>
          <select value={selectedCaption} onChange={e => setSelectedCaption(e.target.value)}>
            <option value="">Select caption...</option>
            {league.draftableCaptions
              .filter(cap => !takenCombos.has(`${selectedCorps}|${cap}`))
              .map(cap => <option key={cap} value={cap}>{cap}</option>)
            }
          </select>
          <button onClick={submitPick} disabled={!selectedCorps || !selectedCaption}>
            Submit Pick
          </button>
        </div>
      )}

      {isCommissioner && !isMyTurn && (
        <button onClick={skipPick}>Skip Current Pick</button>
      )}

      <h3>Pick History</h3>
      <ol>
        {draftState.picks.map(p => (
          <li key={p.pickNumber}>
            {p.displayName} → {p.corpsName} ({p.caption})
          </li>
        ))}
      </ol>
    </div>
  );
}
