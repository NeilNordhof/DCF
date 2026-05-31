import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import type { DraftStatus, League, PublicLeague } from '../types/api';

function formatCountdown(draftStartTime: string): string {
  const diff = new Date(draftStartTime).getTime() - Date.now();

  if (diff <= 0) return 'starting soon';

  const totalMinutes = Math.floor(diff / 60000);
  const days = Math.floor(totalMinutes / 1440);
  const hours = Math.floor((totalMinutes % 1440) / 60);

  if (days > 0) return `in ${days}d ${hours}h`;

  const mins = totalMinutes % 60;
  return `in ${hours}h ${mins}m`;
}

function StatusBadge({ status }: { status: DraftStatus }) {
  const isLive = status === 'InProgress';
  const isOpen = status === 'Open';
  const isScheduled = status === 'Scheduled';

  const base: React.CSSProperties = {
    fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 700,
    textTransform: 'uppercase', letterSpacing: '0.5px',
  };

  if (isLive || isOpen) {
    return (
      <span style={{ ...base, background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)' }}>
        {isLive ? 'Live Draft' : 'Lobby Open'}
      </span>
    );
  }

  if (isScheduled) {
    return (
      <span style={{ ...base, background: 'var(--accent-bg)', color: 'var(--accent)', border: '1px solid var(--accent-border)' }}>
        Scheduled
      </span>
    );
  }

  return (
    <span style={{ ...base, border: '1px solid var(--border)', color: 'var(--text-muted)' }}>
      {status === 'NotStarted' ? 'Not Started' : 'Completed'}
    </span>
  );
}

function LeagueCard({ league }: { league: League }) {
  return (
    <Link
      to={`/leagues/${league.id}`}
      style={{
        display: 'block', padding: '14px 16px',
        background: 'var(--surface)', border: '1px solid var(--border)',
        borderRadius: 6, textDecoration: 'none', color: 'inherit',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
        <span style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-heading)' }}>{league.name}</span>
        <StatusBadge status={league.draftStatus} />
      </div>
      <div style={{ fontSize: 10, color: 'var(--text-muted)', display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        {league.draftStatus === 'InProgress' && (
          <>
            <span>
              {league.userRank != null && league.userScore != null
                ? `Rank ${league.userRank}/${league.memberCount} · ${league.userScore.toFixed(1)} pts`
                : '—'}
            </span>
            <span style={{ color: 'var(--green)', fontWeight: 600 }}>Join Draft Room →</span>
          </>
        )}
        {league.draftStatus === 'Open' && (
          <>
            <span>{league.memberCount} members</span>
            <span style={{ color: 'var(--green)', fontWeight: 600 }}>Join Draft Room →</span>
          </>
        )}
        {league.draftStatus === 'Scheduled' && league.draftStartTime && (
          <span>
            Draft: {new Date(league.draftStartTime).toLocaleDateString()} · ⏱ {formatCountdown(league.draftStartTime)}
          </span>
        )}
        {league.draftStatus === 'NotStarted' && (
          <span>{league.memberCount} members · waiting for commissioner</span>
        )}
        {league.draftStatus === 'Completed' && (
          <span style={{ color: 'var(--text-faint)' }}>
            {league.userRank != null && league.userScore != null
              ? `Rank ${league.userRank}/${league.memberCount} · ${league.userScore.toFixed(1)} pts (final)`
              : `${league.memberCount} members (final)`}
          </span>
        )}
      </div>
    </Link>
  );
}

function MyLeaguesTab() {
  const [leagues, setLeagues] = useState<League[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.getLeagues().then(setLeagues).finally(() => setLoading(false));
  }, []);

  if (loading) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 12 }}>Loading…</p>;
  }

  if (leagues.length === 0) {
    return (
      <p style={{ color: 'var(--text-muted)', fontSize: 12 }}>
        You are not currently in any leagues.{' '}
        <Link to="/leagues?tab=join" style={{ color: 'var(--accent)' }}>Join a league</Link>
        {' '}or{' '}
        <Link to="/leagues/create" style={{ color: 'var(--accent)' }}>Create your own</Link>!
      </p>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      {leagues.map(l => (
        <LeagueCard key={l.id} league={l} />
      ))}
    </div>
  );
}

function JoinTab() {
  const navigate = useNavigate();
  const [code, setCode] = useState('');
  const [codeError, setCodeError] = useState<string | null>(null);
  const [publicLeagues, setPublicLeagues] = useState<PublicLeague[]>([]);
  const [myLeagueIds, setMyLeagueIds] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    Promise.all([api.getPublicLeagues(), api.getLeagues()])
      .then(([pub, mine]) => {
        setPublicLeagues(pub);
        setMyLeagueIds(new Set(mine.map(l => l.id)));
      })
      .finally(() => setLoading(false));
  }, []);

  async function handleLookup() {
    setCodeError(null);

    try {
      const { leagueId } = await api.lookupLeagueByCode(code.trim());
      navigate(`/leagues/${leagueId}?code=${encodeURIComponent(code.trim())}`);
    } catch {
      setCodeError('No league found with that code.');
    }
  }

  return (
    <div>
      <p style={{ color: 'var(--text-muted)', fontSize: 12, marginBottom: 14 }}>
        Browse and join a public league, or join by code:
      </p>
      <div style={{ display: 'flex', gap: 8, marginBottom: 6 }}>
        <input
          style={{
            flex: 1, background: 'var(--surface)', border: '1px solid var(--border)',
            borderRadius: 5, padding: '7px 10px', fontSize: 11,
            color: 'var(--text-heading)', outline: 'none',
          }}
          placeholder="Invite code"
          value={code}
          onChange={e => setCode(e.target.value)}
          onKeyDown={e => e.key === 'Enter' && code.trim() && handleLookup()}
        />
        <button
          style={{
            fontSize: 11, fontWeight: 700, padding: '7px 14px', borderRadius: 5,
            background: 'var(--surface)', border: '1px solid var(--border)',
            color: 'var(--text-heading)', cursor: code.trim() ? 'pointer' : 'not-allowed',
            opacity: code.trim() ? 1 : 0.5,
          }}
          onClick={handleLookup}
          disabled={!code.trim()}
        >
          Look up
        </button>
      </div>
      {codeError && (
        <p style={{ color: '#e06c75', fontSize: 11, marginBottom: 12 }}>{codeError}</p>
      )}

      <div style={{ marginTop: 20 }}>
        {loading ? (
          <p style={{ color: 'var(--text-muted)', fontSize: 12 }}>Loading…</p>
        ) : (() => {
          const joinable = publicLeagues.filter(l => !myLeagueIds.has(l.id));

          if (joinable.length === 0) {
            return (
              <p style={{ color: 'var(--text-muted)', fontSize: 12 }}>
                {publicLeagues.length === 0
                  ? <>There are no public leagues to join.{' '}<Link to="/leagues/create" style={{ color: 'var(--accent)' }}>Create one now!</Link></>
                  : "You've already joined all public leagues."}
              </p>
            );
          }

          return (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            {joinable.map(l => (
              <Link
                key={l.id}
                to={`/leagues/${l.id}`}
                style={{
                  display: 'flex', alignItems: 'center', justifyContent: 'space-between',
                  padding: '10px 14px', background: 'var(--surface)',
                  border: '1px solid var(--border)', borderRadius: 5,
                  textDecoration: 'none', color: 'inherit',
                }}
              >
                <span style={{ fontSize: 12, fontWeight: 600, color: 'var(--text-heading)' }}>{l.name}</span>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>
                    {l.memberCount} / {l.maxPlayers} members
                  </span>
                  <StatusBadge status={l.draftStatus} />
                </div>
              </Link>
            ))}
          </div>
          );
        })()}
      </div>
    </div>
  );
}

export function Leagues() {
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = searchParams.get('tab') === 'join' ? 'join' : 'my';

  function setTab(t: 'my' | 'join') {
    if (t === 'my') {
      setSearchParams({});
    } else {
      setSearchParams({ tab: 'join' });
    }
  }

  const tabStyle = (active: boolean): React.CSSProperties => ({
    fontSize: 11, fontWeight: active ? 800 : 600,
    padding: '6px 16px', borderRadius: 4, border: 'none',
    background: active ? 'var(--accent)' : 'transparent',
    color: active ? 'var(--bg)' : 'var(--text-muted)',
    cursor: 'pointer', letterSpacing: '0.3px',
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)' }}>Leagues</h2>
        <Link
          to="/leagues/create"
          style={{
            fontSize: 11, fontWeight: 800, padding: '6px 14px', borderRadius: 5,
            background: 'var(--accent)', color: 'var(--bg)', textDecoration: 'none',
            letterSpacing: '0.5px',
          }}
        >
          + Create League
        </Link>
      </div>

      <div style={{ display: 'flex', gap: 4, borderBottom: '1px solid var(--border)', paddingBottom: 12 }}>
        <button style={tabStyle(tab === 'my')} onClick={() => setTab('my')}>My Leagues</button>
        <button style={tabStyle(tab === 'join')} onClick={() => setTab('join')}>Join</button>
      </div>

      {tab === 'my' ? <MyLeaguesTab /> : <JoinTab />}
    </div>
  );
}
