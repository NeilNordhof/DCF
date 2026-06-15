import { useEffect, useState } from 'react';
import type { CSSProperties, FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { Corps, SeasonDetail as SeasonDetailType, Show } from '../types/api';
import { CorpsIcon } from '../components/CorpsIcon';
import { TimePicker } from '../components/TimePicker';

const TZ_HOURS: Record<string, number> = { PT: 7, MT: 6, CT: 5, ET: 4 };

function buildDateTime(date: string, time: string, tz: string): string {
  const d = new Date(`${date}T${time}:00Z`);
  d.setUTCHours(d.getUTCHours() + (TZ_HOURS[tz] ?? 4));
  return d.toISOString();
}

function hasStarted(show: Show): boolean {
  return !!show.startTime && new Date(show.startTime) <= new Date();
}

function hasScoresAnnounced(show: Show): boolean {
  return new Date(show.scoresAnnouncedTime) <= new Date();
}

const inputStyle: CSSProperties = {
  width: '100%', padding: '7px 10px', borderRadius: 5,
  background: 'var(--bg)', border: '1px solid var(--border-input)',
  color: 'var(--text-heading)', fontSize: 11, outline: 'none',
};

const labelStyle: CSSProperties = {
  fontSize: 9, fontWeight: 700, color: 'var(--text-faint)',
  textTransform: 'uppercase', letterSpacing: '0.5px',
  minWidth: 48, textAlign: 'right', flexShrink: 0,
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
  const [showPublishConfirm, setShowPublishConfirm] = useState(false);

  const [showName, setShowName] = useState('');
  const [showUrl, setShowUrl] = useState('');
  const [showDate, setShowDate] = useState('');
  const [showTz, setShowTz] = useState('ET');
  const [showStartTime, setShowStartTime] = useState('');
  const [showScoresTime, setShowScoresTime] = useState('');
  const [addShowOpen, setAddShowOpen] = useState(false);
  const [showCorpsIds, setShowCorpsIds] = useState<Set<string>>(new Set());
  const [addingShow, setAddingShow] = useState(false);

  const [expandedShowId, setExpandedShowId] = useState<string | null>(null);
  const [editShow, setEditShow] = useState<{
    name: string; url: string; date: string;
    startTime: string; scoresTime: string; tz: string;
    corpsIds: Set<string>;
  } | null>(null);
  const [savingShowEdit, setSavingShowEdit] = useState(false);
  const [deletingShowId, setDeletingShowId] = useState<string | null>(null);

  const [error, setError] = useState<string | null>(null);
  const [scrapeSuccessId, setScrapeSuccessId] = useState<string | null>(null);

  const [editingDates, setEditingDates] = useState(false);
  const [editStartDate, setEditStartDate] = useState('');
  const [editEndDate, setEditEndDate] = useState('');
  const [savingDates, setSavingDates] = useState(false);

  const [corpsSortInputs, setCorpsSortInputs] = useState<Record<string, string>>({});
  const [savingOrder, setSavingOrder] = useState(false);

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
      setCorpsSortInputs(
        Object.fromEntries(
          Object.entries(s.corpsSortOrders ?? {}).map(([corpsId, order]) => [corpsId, String(order)])
        )
      );
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

  const saveCorpsOrder = async () => {
    if (!id || savingOrder) return;
    setSavingOrder(true);
    setError(null);

    try {
      const orders = Object.entries(corpsSortInputs).map(([corpsId, val]) => ({
        corpsId,
        sortOrder: parseInt(val) > 0 ? parseInt(val) : null,
      }));
      await api.adminSetCorpsOrder(id, orders);
      const updated = await api.adminGetSeason(id);
      setSeason(updated);
      setCorpsSortInputs(
        Object.fromEntries(
          Object.entries(updated.corpsSortOrders ?? {}).map(([corpsId, order]) => [corpsId, String(order)])
        )
      );
    } catch {
      setError('Failed to save order.');
    } finally {
      setSavingOrder(false);
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
      const startTimeIso = showStartTime ? buildDateTime(showDate, showStartTime, showTz) : null;
      const scoresTimeIso = buildDateTime(showDate, showScoresTime, showTz);

      await api.adminCreateShow(id, showName, showUrl, showDate, startTimeIso, scoresTimeIso, showTz, Array.from(showCorpsIds));
      const updated = await api.adminGetShows(id);
      setShows(updated);
      setShowName('');
      setShowUrl('');
      setShowDate('');
      setShowStartTime('');
      setShowScoresTime('');
      setShowCorpsIds(new Set());
      setAddShowOpen(false);
    } catch {
      setError('Failed to add show.');
    } finally {
      setAddingShow(false);
    }
  };

  const saveDates = async () => {
    if (!id || savingDates) return;
    setSavingDates(true);
    setError(null);

    try {
      await api.adminUpdateSeasonDates(id, editStartDate, editEndDate);
      const updated = await api.adminGetSeason(id);
      setSeason(updated);
      setEditingDates(false);
    } catch {
      setError('Failed to update season dates.');
    } finally {
      setSavingDates(false);
    }
  };

  function expandShow(show: Show) {
    if (expandedShowId === show.id) {
      setExpandedShowId(null);
      setEditShow(null);
      return;
    }
    const tz = show.timezone ?? 'ET';
    const toHHMM = (iso: string) => {
      const d = new Date(iso);
      d.setUTCHours(d.getUTCHours() - (TZ_HOURS[tz] ?? 4));
      return d.toISOString().slice(11, 16);
    };
    setExpandedShowId(show.id);
    setEditShow({
      name: show.name,
      url: show.url,
      date: show.date,
      startTime: show.startTime ? toHHMM(show.startTime) : '',
      scoresTime: toHHMM(show.scoresAnnouncedTime),
      tz,
      corpsIds: new Set(show.corpsIds),
    });
  }

  const saveShowEdit = async (showId: string) => {
    if (!editShow || savingShowEdit) return;
    setSavingShowEdit(true);
    setError(null);

    try {
      const startTimeIso = editShow.startTime
        ? buildDateTime(editShow.date, editShow.startTime, editShow.tz)
        : null;
      const scoresTimeIso = buildDateTime(editShow.date, editShow.scoresTime, editShow.tz);

      await api.adminUpdateShow(showId, {
        name: editShow.name,
        url: editShow.url,
        date: editShow.date,
        startTime: startTimeIso,
        scoresAnnouncedTime: scoresTimeIso,
        timezone: editShow.tz,
        corpsIds: Array.from(editShow.corpsIds),
      });

      const updated = await api.adminGetShows(id!);
      setShows(updated);
      setExpandedShowId(null);
      setEditShow(null);
    } catch {
      setError('Failed to save show.');
    } finally {
      setSavingShowEdit(false);
    }
  };

  const deleteShow = async (showId: string) => {
    if (deletingShowId) return;
    setDeletingShowId(showId);
    setError(null);

    try {
      await api.adminDeleteShow(showId);
      const updated = await api.adminGetShows(id!);
      setShows(updated);
      setExpandedShowId(null);
      setEditShow(null);
    } catch {
      setError('Failed to delete show.');
    } finally {
      setDeletingShowId(null);
    }
  };

  if (!season) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>{error ?? 'Loading…'}</div>;
  }

  const seasonCorps = allCorps.filter(c => season.corpsIds.includes(c.id));

  const sortedSeasonCorps = [...seasonCorps].sort((a, b) => {
    const aVal = parseInt(corpsSortInputs[a.id] ?? '');
    const bVal = parseInt(corpsSortInputs[b.id] ?? '');
    const aRanked = !isNaN(aVal) && aVal > 0;
    const bRanked = !isNaN(bVal) && bVal > 0;
    if (aRanked && bRanked) return aVal - bVal;
    if (aRanked) return -1;
    if (bRanked) return 1;
    return a.name.localeCompare(b.name);
  });

  return (
    <div>
      {showPublishConfirm && (
        <div style={{
          position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)',
          display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100,
        }}>
          <div style={{
            background: 'var(--surface)', border: '1px solid var(--border)',
            borderRadius: 8, padding: 28, maxWidth: 360, width: '90%',
          }}>
            <h2 style={{ fontSize: 14, fontWeight: 800, color: 'var(--text-heading)', marginBottom: 10 }}>
              Publish Season {season.year}?
            </h2>
            <p style={{ fontSize: 11, color: 'var(--text-muted)', marginBottom: 20 }}>
              Once published, the corps roster is locked and cannot be changed.
            </p>
            <div style={{ display: 'flex', gap: 10 }}>
              <button
                onClick={() => setShowPublishConfirm(false)}
                style={{
                  flex: 1, padding: '8px 0', borderRadius: 5, fontSize: 11, fontWeight: 700,
                  background: 'var(--surface)', border: '1px solid var(--border)',
                  color: 'var(--text-heading)', cursor: 'pointer',
                }}
              >
                Cancel
              </button>
              <button
                onClick={() => { setShowPublishConfirm(false); publish(); }}
                style={{
                  flex: 1, padding: '8px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                  background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer',
                }}
              >
                Publish
              </button>
            </div>
          </div>
        </div>
      )}
      <div style={{ display: 'flex', alignItems: 'flex-start', justifyContent: 'space-between', marginBottom: 24 }}>
        <div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
            <Link to="/admin" style={{ fontSize: 10, color: 'var(--text-muted)', textDecoration: 'none' }}>← Admin</Link>
          </div>
          <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)', marginBottom: 4 }}>Season {season.year}</h2>
          {editingDates ? (
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 4 }}>
              <input type="date" value={editStartDate} onChange={e => setEditStartDate(e.target.value)} style={{ ...inputStyle, width: 130 }} />
              <span style={{ color: 'var(--text-muted)', fontSize: 10 }}>–</span>
              <input type="date" value={editEndDate} onChange={e => setEditEndDate(e.target.value)} style={{ ...inputStyle, width: 130 }} />
              <button onClick={saveDates} disabled={savingDates} style={{ padding: '5px 10px', borderRadius: 4, fontSize: 10, fontWeight: 700, background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer' }}>
                Save
              </button>
              <button onClick={() => setEditingDates(false)} style={{ padding: '5px 10px', borderRadius: 4, fontSize: 10, background: 'transparent', border: '1px solid var(--border)', color: 'var(--text-muted)', cursor: 'pointer' }}>
                Cancel
              </button>
            </div>
          ) : (
            <div style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 10, color: 'var(--text-muted)', marginTop: 4 }}>
              <span>{season.startDate} – {season.endDate} · {season.status}</span>
              {!season.isPublished && (
                <button
                  onClick={() => { setEditingDates(true); setEditStartDate(season.startDate); setEditEndDate(season.endDate); }}
                  style={{ fontSize: 9, background: 'transparent', border: 'none', color: 'var(--accent)', cursor: 'pointer', padding: '2px 4px' }}
                >
                  Edit
                </button>
              )}
            </div>
          )}
        </div>
        {!season.isPublished && season.corpsIds.length > 0 && (
          <button
            onClick={() => setShowPublishConfirm(true)}
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
          {seasonCorps.length > 0 && (
            <>
              <div style={{ height: 1, background: 'var(--border)', margin: '4px 0 12px' }} />
              <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 4 }}>Draft Order</div>
              {!season.isPublished && (
                <div style={{ fontSize: 9, color: 'var(--text-faint)', marginBottom: 10 }}>
                  Enter prior season placements. List re-sorts as you type.
                </div>
              )}
              <div style={{ display: 'flex', flexDirection: 'column', gap: 4, marginBottom: 10 }}>
                {sortedSeasonCorps.map(c => {
                  const val = corpsSortInputs[c.id] ?? '';
                  const isUnranked = val === '' || !(parseInt(val) > 0);
                  return (
                    <div key={c.id} style={{
                      display: 'flex', alignItems: 'center', gap: 8,
                      padding: '5px 10px', background: 'var(--surface)',
                      border: '1px solid var(--border)', borderRadius: 4,
                    }}>                      
                      <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={22} />
                      <span style={{ fontSize: 11, color: isUnranked ? 'var(--text-muted)' : 'var(--text-heading)', flex: 1 }}>
                        {c.name}
                      </span>
                      <input
                        type="number"
                        min={1}
                        value={val}
                        onChange={e => setCorpsSortInputs(prev => ({ ...prev, [c.id]: e.target.value }))}
                        disabled={season.isPublished}
                        placeholder="–"
                        style={{
                          width: 60, background: 'var(--bg)',
                          border: `1px ${isUnranked ? 'dashed' : 'solid'} var(--border-input)`,
                          borderRadius: 3, padding: '3px 5px',
                          color: isUnranked ? 'var(--text-faint)' : 'var(--text-heading)',
                          fontSize: 10, textAlign: 'center',
                          opacity: season.isPublished ? 0.5 : 1,
                          outline: 'none',
                        }}
                      />
                    </div>
                  );
                })}
              </div>
              {!season.isPublished && (
                <button
                  onClick={saveCorpsOrder}
                  disabled={savingOrder}
                  style={{
                    padding: '7px 14px', borderRadius: 5, fontSize: 11, fontWeight: 800,
                    background: savingOrder ? 'var(--border)' : 'var(--accent)',
                    color: savingOrder ? 'var(--text-faint)' : 'var(--bg)',
                    border: 'none', cursor: savingOrder ? 'not-allowed' : 'pointer',
                  }}
                >
                  {savingOrder ? 'Saving…' : 'Save Order'}
                </button>
              )}
            </>
          )}
        </div>

        <div style={{ flex: 1, display: 'flex', flexDirection: 'column', gap: 12 }}>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Shows</div>

          {/* Add Show — collapsible */}
          <div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5 }}>
            <button
              type="button"
              onClick={() => setAddShowOpen(o => !o)}
              style={{
                width: '100%', display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                padding: '10px 14px', background: 'transparent', border: 'none', cursor: 'pointer',
                fontSize: 8, fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.5px',
                color: 'var(--text-faint)',
              }}
            >
              <span>Add Show</span>
              <span style={{ fontSize: 10 }}>{addShowOpen ? '▲' : '▼'}</span>
            </button>

            {addShowOpen && (
              <form onSubmit={addShow} style={{ padding: '0 14px 14px', display: 'flex', flexDirection: 'column', gap: 10 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <label style={labelStyle}>Name</label>
                  <input value={showName} onChange={e => setShowName(e.target.value)} required style={{ ...inputStyle, flex: 1 }} />
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <label style={labelStyle}>URL</label>
                  <input value={showUrl} onChange={e => setShowUrl(e.target.value)} placeholder="DCI recap URL" required style={{ ...inputStyle, flex: 1 }} />
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <label style={{ ...labelStyle }}>Date</label>
                  <input type="date" value={showDate} onChange={e => setShowDate(e.target.value)} required style={{ ...inputStyle, flex: 1 }} />
                  <label style={{ ...labelStyle, marginLeft: 8 }}>TZ</label>
                  <select value={showTz} onChange={e => setShowTz(e.target.value)} style={{ ...inputStyle, width: 62 }}>
                    {['ET', 'CT', 'MT', 'PT'].map(tz => <option key={tz} value={tz}>{tz}</option>)}
                  </select>
                </div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <label style={labelStyle}>Start</label>
                  <TimePicker value={showStartTime} onChange={setShowStartTime} style={{ flex: 1 }} />
                  <label style={{ ...labelStyle, marginLeft: 8 }}>Scores</label>
                  <TimePicker value={showScoresTime} onChange={setShowScoresTime} required style={{ flex: 1 }} />
                </div>
                <div>
                  <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6 }}>Participating Corps</div>
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                    {seasonCorps.map(c => (
                      <Chip key={c.id} label={c.name} selected={showCorpsIds.has(c.id)} onClick={() => toggleShowCorps(c.id)} />
                    ))}
                  </div>
                </div>
                <button type="submit" disabled={addingShow} style={{
                  padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                  background: addingShow ? 'var(--border)' : 'var(--accent)',
                  color: addingShow ? 'var(--text-faint)' : 'var(--bg)',
                  border: 'none', cursor: addingShow ? 'not-allowed' : 'pointer',
                }}>
                  {addingShow ? 'Adding…' : 'Add Show'}
                </button>
              </form>
            )}
          </div>

          {shows.length === 0 && (
            <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>No shows yet.</div>
          )}

          {shows.map(s => {
            const expanded = expandedShowId === s.id;
            const started = hasStarted(s);

            return (
              <div key={s.id} style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, overflow: 'hidden' }}>
                <div
                  onClick={() => expandShow(s)}
                  style={{ display: 'flex', alignItems: 'center', padding: '10px 14px', cursor: 'pointer', gap: 8 }}
                >
                  <div style={{ flex: 1 }}>
                    <div style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-heading)' }}>{s.name}</div>
                    <div style={{ fontSize: 9, color: 'var(--text-muted)' }}>
                      {s.date}
                      {s.startTime && ` · starts ${new Date(s.startTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`}
                      {s.scoresAnnouncedTime && ` · scores ${new Date(s.scoresAnnouncedTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`}
                      {hasScoresAnnounced(s)
                        ? <span style={{ color: 'var(--green)', marginLeft: 6, fontWeight: 700, fontSize: 8 }}>SCORES ANNOUNCED</span>
                        : started && <span style={{ color: 'var(--accent)', marginLeft: 6, fontWeight: 700, fontSize: 8 }}>STARTED</span>
                      }
                    </div>
                  </div>
                  <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{expanded ? '▲' : '▼'}</span>
                </div>

                {expanded && editShow && (
                  <div style={{ padding: '0 14px 14px', borderTop: '1px solid var(--border)', display: 'flex', flexDirection: 'column', gap: 10 }}>
                    {!started && !hasScoresAnnounced(s) ? (
                      <>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginTop: 10 }}>
                          <label style={labelStyle}>Name</label>
                          <input value={editShow.name} onChange={e => setEditShow(p => p && ({ ...p, name: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <label style={labelStyle}>URL</label>
                          <input value={editShow.url} onChange={e => setEditShow(p => p && ({ ...p, url: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <label style={labelStyle}>Date</label>
                          <input type="date" value={editShow.date} onChange={e => setEditShow(p => p && ({ ...p, date: e.target.value }))} style={{ ...inputStyle, flex: 1 }} />
                          <label style={{ ...labelStyle, marginLeft: 8 }}>TZ</label>
                          <select value={editShow.tz} onChange={e => setEditShow(p => p && ({ ...p, tz: e.target.value }))} style={{ ...inputStyle, width: 62 }}>
                            {['ET', 'CT', 'MT', 'PT'].map(tz => <option key={tz} value={tz}>{tz}</option>)}
                          </select>
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                          <label style={labelStyle}>Start</label>
                          <TimePicker value={editShow.startTime} onChange={v => setEditShow(p => p && ({ ...p, startTime: v }))} style={{ flex: 1 }} />
                          <label style={{ ...labelStyle, marginLeft: 8 }}>Scores</label>
                          <TimePicker value={editShow.scoresTime} onChange={v => setEditShow(p => p && ({ ...p, scoresTime: v }))} required style={{ flex: 1 }} />
                        </div>
                        <div>
                          <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6 }}>Participating Corps</div>
                          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                            {seasonCorps.map(c => (
                              <Chip
                                key={c.id}
                                label={c.name}
                                selected={editShow.corpsIds.has(c.id)}
                                onClick={() => setEditShow(p => {
                                  if (!p) return p;
                                  const next = new Set(p.corpsIds);
                                  if (next.has(c.id)) next.delete(c.id); else next.add(c.id);
                                  return { ...p, corpsIds: next };
                                })}
                              />
                            ))}
                          </div>
                        </div>
                        <div style={{ display: 'flex', gap: 8 }}>
                          <button
                            type="button"
                            onClick={() => deleteShow(s.id)}
                            disabled={!!deletingShowId}
                            style={{
                              flex: 1, padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 700,
                              background: 'transparent', border: '1px solid var(--red)', color: 'var(--red)',
                              cursor: deletingShowId ? 'not-allowed' : 'pointer',
                              opacity: deletingShowId === s.id ? 0.5 : 1,
                            }}
                          >
                            {deletingShowId === s.id ? 'Deleting…' : 'Delete Show'}
                          </button>
                          <button
                            type="button"
                            onClick={() => saveShowEdit(s.id)}
                            disabled={savingShowEdit}
                            style={{
                              flex: 2, padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                              background: savingShowEdit ? 'var(--border)' : 'var(--accent)',
                              color: savingShowEdit ? 'var(--text-faint)' : 'var(--bg)',
                              border: 'none', cursor: savingShowEdit ? 'not-allowed' : 'pointer',
                            }}
                          >
                            {savingShowEdit ? 'Saving…' : 'Save'}
                          </button>
                        </div>
                      </>
                    ) : (
                      <div style={{ marginTop: 10 }}>
                        <button
                          type="button"
                          onClick={() => {
                            api.adminTriggerScrape(s.id)
                              .then(() => {
                                setError(null);
                                setScrapeSuccessId(s.id);
                                setTimeout(() => setScrapeSuccessId(null), 3000);
                              })
                              .catch(() => setError('Scrape trigger failed.'));
                          }}
                          style={{
                            width: '100%', padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
                            background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer',
                          }}
                        >
                          Trigger Score Scrape
                        </button>
                        {scrapeSuccessId === s.id && (
                          <div style={{ marginTop: 6, fontSize: 11, fontWeight: 600, color: 'var(--green)', textAlign: 'center' }}>
                            ✓ Scrape triggered successfully
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}
