import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useUser } from '../context/UserContext';
import type { League, Standing } from '../types/api';

function StatusBadge({ status }: { status: string }) {
  const isLive = status === 'InProgress';
  const isOpen = status === 'Open';

  if (isLive || isOpen) {
    return (
      <span style={{
        fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 700,
        textTransform: 'uppercase', letterSpacing: '0.5px',
        background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)',
      }}>
        {isLive ? 'LIVE DRAFT' : 'LOBBY OPEN'}
      </span>
    );
  }

  return (
    <span style={{
      fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 600,
      textTransform: 'uppercase', letterSpacing: '0.5px',
      border: '1px solid var(--border)', color: 'var(--text-muted)',
    }}>
      {status === 'NotStarted' ? 'NOT STARTED' : status === 'Scheduled' ? 'SCHEDULED' : status === 'Completed' ? 'COMPLETED' : status}
    </span>
  );
}

export function Leagues() {
  const { user } = useUser();
  const navigate = useNavigate();
  const [leagues, setLeagues] = useState<League[]>([]);
  const [featuredStandings, setFeaturedStandings] = useState<Standing[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.getLeagues().then(setLeagues).catch(() => setError('Failed to load leagues.'));
  }, []);

  const featured = leagues.find(l => l.isMember && (l.draftStatus === 'Open' || l.draftStatus === 'InProgress'));
  const others = leagues.filter(l => l !== featured);

  useEffect(() => {
    if (!featured) {
      setFeaturedStandings([]);
      return;
    }
    api.getStandings(featured.id).then(setFeaturedStandings).catch(() => {});
  }, [featured]);

  const userRank = featuredStandings.findIndex(s => s.userId === user?.id) + 1;
  const userScore = featuredStandings.find(s => s.userId === user?.id)?.score ?? 0;

  if (error) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>{error}</div>;
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24 }}>
      {/* Header row */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)' }}>My Leagues</h2>
        <Link
          to="/leagues/create"
          style={{
            fontSize: 11, fontWeight: 800, padding: '6px 14px', borderRadius: 5,
            background: 'var(--accent)', color: 'var(--bg)', textDecoration: 'none',
            letterSpacing: '0.5px',
          }}
        >
          + New
        </Link>
      </div>

      {/* Empty state */}
      {leagues.length === 0 && (
        <div style={{
          textAlign: 'center', padding: '60px 20px',
          border: '1px solid var(--border)', borderRadius: 6, color: 'var(--text-muted)',
        }}>
          <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-heading)', marginBottom: 8 }}>No leagues yet</div>
          <div style={{ fontSize: 11, marginBottom: 20 }}>Create a league or ask a commissioner for an invite code.</div>
          <Link
            to="/leagues/create"
            style={{
              fontSize: 11, fontWeight: 800, padding: '7px 16px', borderRadius: 5,
              background: 'var(--accent)', color: 'var(--bg)', textDecoration: 'none',
            }}
          >
            Create League
          </Link>
        </div>
      )}

      {/* Featured card */}
      {featured && (
        <div style={{
          background: 'linear-gradient(135deg, var(--surface-deep), var(--surface))',
          border: '1px solid var(--accent-border)',
          borderRadius: 6,
          padding: 24,
        }}>
          <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 20 }}>
            <div>
              <div style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)', marginBottom: 6 }}>{featured.name}</div>
              <StatusBadge status={featured.draftStatus} />
            </div>
            <button
              onClick={() => navigate(`/leagues/${featured.id}/draft`)}
              style={{
                fontSize: 11, fontWeight: 800, padding: '7px 16px', borderRadius: 5,
                background: 'var(--accent)', color: 'var(--bg)', border: 'none',
                letterSpacing: '0.5px',
              }}
            >
              Draft Room →
            </button>
          </div>
          <div style={{ display: 'flex', gap: 20 }}>
            {[
              { label: 'Rank', value: userRank > 0 ? `#${userRank}` : '—' },
              { label: 'Points', value: userScore > 0 ? userScore.toFixed(2) : '—' },
              { label: 'Members', value: String(featured.memberCount ?? '—') },
            ].map(stat => (
              <div key={stat.label} style={{ flex: 1, background: 'rgba(0,0,0,0.25)', borderRadius: 5, padding: '10px 14px' }}>
                <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 4 }}>{stat.label}</div>
                <div style={{ fontSize: 16, fontWeight: 900, color: 'var(--accent)' }}>{stat.value}</div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Other leagues */}
      {others.length > 0 && (
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 10 }}>
            {featured ? 'Other Leagues' : 'All Leagues'}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
            {others.map(l => (
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
                  <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{l.memberCount ?? 0} members</span>
                  <StatusBadge status={l.draftStatus} />
                  <span style={{ color: 'var(--text-muted)', fontSize: 14 }}>›</span>
                </div>
              </Link>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
