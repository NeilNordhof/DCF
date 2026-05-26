import { useEffect, useState } from 'react';
import type { CSSProperties, FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { Corps, SeasonDetail as SeasonDetailType, Show } from '../types/api';

const inputStyle: CSSProperties = {
  width: '100%', padding: '7px 10px', borderRadius: 5,
  background: 'var(--bg)', border: '1px solid var(--border-input)',
  color: 'var(--text-heading)', fontSize: 11, outline: 'none',
};

function Chip({ label, selected, onClick, disabled }: { label: string; selected: boolean; onClick: () => void; disabled?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      style={{
        padding: '5px 12px', borderRadius: 5, fontSize: 10, fontWeight: selected ? 600 : 500,
        cursor: disabled ? 'not-allowed' : 'pointer',
        border: `1px solid ${selected ? 'var(--green-border)' : 'var(--border)'}`,
        background: selected ? 'var(--green-bg)' : 'var(--surface)',
        color: selected ? 'var(--green)' : 'var(--text-muted)',
        opacity: disabled ? 0.55 : 1,
      }}
    >
      {label}
    </button>
  );
}

export function SeasonDetail() {
  const { id } = useParams<{ id: string }>();
  const [season, setSeason] = useState<SeasonDetailType | null>(null);
  const [allCorps, setAllCorps] = useState<Corps[]>([]);
  const [shows, setShows] = useState<Show[]>([]);
  const [selectedCorpsIds, setSelectedCorpsIds] = useState<Set<string>>(new Set());
  const [savingCorps, setSavingCorps] = useState(false);
  const [publishing, setPublishing] = useState(false);

  const [showName, setShowName] = useState('');
  const [showUrl, setShowUrl] = useState('');
  const [showDate, setShowDate] = useState('');
  const [showScoresTime, setShowScoresTime] = useState('');
  const [showCorpsIds, setShowCorpsIds] = useState<Set<string>>(new Set());
  const [addingShow, setAddingShow] = useState(false);

  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    let cancelled = false;

    Promise.all([
      api.adminGetSeason(id),
      api.adminGetCorps(),
      api.adminGetShows(id),
    ]).then(([s, c, sh]) => {
      if (cancelled) return;
      setSeason(s);
      setAllCorps(c);
      setShows(sh);
      setSelectedCorpsIds(new Set(s.corpsIds));
    }).catch(() => { if (!cancelled) setError('Failed to load season.'); });

    return () => { cancelled = true; };
  }, [id]);

  const toggleCorps = (corpsId: string) => {
    setSelectedCorpsIds(prev => {
      const next = new Set(prev);
      if (next.has(corpsId)) next.delete(corpsId); else next.add(corpsId);
      return next;
    });
  };

  const saveCorps = async (e: FormEvent) => {
    e.preventDefault();
    if (!id || savingCorps) return;
    setSavingCorps(true);
    setError(null);

    try {
      await api.adminSetSeasonCorps(id, Array.from(selectedCorpsIds));
      const updated = await api.adminGetSeason(id);
      setSeason(updated);
      setSelectedCorpsIds(new Set(updated.corpsIds));
    } catch {
      setError('Failed to save corps.');
    } finally {
      setSavingCorps(false);
    }
  };

  const publish = async () => {
    if (!id || publishing) return;
    setPublishing(true);
    setError(null);

    try {
      await api.adminPublishSeason(id);
      const updated = await api.adminGetSeason(id);
      setSeason(updated);
    } catch {
      setError('Failed to publish season.');
    } finally {
      setPublishing(false);
    }
  };

  const toggleShowCorps = (corpsId: string) => {
    setShowCorpsIds(prev => {
      const next = new Set(prev);
      if (next.has(corpsId)) next.delete(corpsId); else next.add(corpsId);
      return next;
    });
  };

  const addShow = async (e: FormEvent) => {
    e.preventDefault();
    if (!id || addingShow) return;
    if (showCorpsIds.size === 0) { setError('Select at least one corps.'); return; }
    setAddingShow(true);
    setError(null);

    try {
      await api.adminCreateShow(id, showName, showUrl, showDate, new Date(showScoresTime).toISOString(), Array.from(showCorpsIds));
      const updated = await api.adminGetShows(id);
      setShows(updated);
      setShowName('');
      setShowUrl('');
      setShowDate('');
      setShowScoresTime('');
      setShowCorpsIds(new Set());
    } catch {
      setError('Failed to add show.');
    } finally {
      setAddingShow(false);
    }
  };

  if (!season) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>{error ?? 'Loading…'}</div>;
  }

  const seasonCorps = allCorps.filter(c => season.corpsIds.includes(c.id));

  return (
    <div>
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 24 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
            <Link to="/admin" style={{ fontSize: 10, color: 'var(--text-muted)', textDecoration: 'none' }}>← Admin</Link>
          </div>
          <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)', marginBottom: 4 }}>Season {season.year}</h2>
          <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{season.startDate} – {season.endDate} · {season.status}</div>
        </div>
        {!season.isPublished && season.corpsIds.length > 0 && (
          <button
            onClick={publish}
            disabled={publishing}
            style={{
              padding: '7px 16px', borderRadius: 5, fontSize: 11, fontWeight: 800,
              background: publishing ? 'var(--border)' : 'var(--accent)',
              color: publishing ? 'var(--text-faint)' : 'var(--bg)',
              border: 'none', cursor: publishing ? 'not-allowed' : 'pointer',
            }}
          >
            {publishing ? 'Publishing…' : 'Publish'}
          </button>
        )}
        {season.isPublished && (
          <span style={{ fontSize: 8, padding: '4px 10px', borderRadius: 4, fontWeight: 700, background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)' }}>PUBLISHED</span>
        )}
      </div>

      {error && <div style={{ fontSize: 10, color: 'var(--red)', marginBottom: 16 }}>{error}</div>}

      <div style={{ display: 'flex', gap: 20, alignItems: 'flex-start' }}>
        <div style={{ flex: '0 0 280px', display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Corps this season</div>
          <form onSubmit={saveCorps}>
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6, marginBottom: 12 }}>
              {allCorps.map(c => (
                <Chip
                  key={c.id}
                  label={c.name}
                  selected={selectedCorpsIds.has(c.id)}
                  onClick={() => toggleCorps(c.id)}
                  disabled={season.isPublished}
                />
              ))}
            </div>
            <button
              type="submit"
              disabled={savingCorps || season.isPublished}
              style={{
                padding: '7px 14px', borderRadius: 5, fontSize: 11, fontWeight: 800,
                background: savingCorps || season.isPublished ? 'var(--border)' : 'var(--accent)',
                color: savingCorps || season.isPublished ? 'var(--text-faint)' : 'var(--bg)',
                border: 'none', cursor: savingCorps || season.isPublished ? 'not-allowed' : 'pointer',
              }}
            >
              {season.isPublished ? 'Locked (published)' : savingCorps ? 'Saving…' : 'Save Corps'}
            </button>
          </form>
        </div>

        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Shows</div>

          {shows.length === 0 && (
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>No shows yet.</div>
          )}

          {shows.map(s => (
            <div key={s.id} style={{
              padding: '12px 14px', background: 'var(--surface)',
              border: '1px solid var(--border)', borderRadius: 5,
            }}>
              <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-heading)', marginBottom: 3 }}>{s.name}</div>
              <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{s.date}</div>
              <div style={{ fontSize: 9, color: 'var(--text-faint)', marginTop: 2 }}>
                Scores at {new Date(s.scoresAnnouncedTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
              </div>
            </div>
          ))}

          <div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, padding: 16 }}>
            <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 12 }}>Add Show</div>
            <form onSubmit={addShow} style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              <input value={showName} onChange={e => setShowName(e.target.value)} placeholder="Show name" required style={inputStyle} />
              <input value={showUrl} onChange={e => setShowUrl(e.target.value)} placeholder="DCI recap URL" required style={inputStyle} />
              <input type="date" value={showDate} onChange={e => setShowDate(e.target.value)} required style={inputStyle} />
              <input type="datetime-local" value={showScoresTime} onChange={e => setShowScoresTime(e.target.value)} required style={inputStyle} />

              <div>
                <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6 }}>Participating Corps</div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {seasonCorps.map(c => (
                    <Chip
                      key={c.id}
                      label={c.name}
                      selected={showCorpsIds.has(c.id)}
                      onClick={() => toggleShowCorps(c.id)}
                    />
                  ))}
                </div>
              </div>

              <button
                type="submit"
                disabled={addingShow}
                style={{
                  padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                  background: addingShow ? 'var(--border)' : 'var(--accent)',
                  color: addingShow ? 'var(--text-faint)' : 'var(--bg)',
                  border: 'none', cursor: addingShow ? 'not-allowed' : 'pointer',
                }}
              >
                {addingShow ? 'Adding…' : 'Add Show'}
              </button>
            </form>
          </div>
        </div>
      </div>
    </div>
  );
}
