import { useEffect, useRef, useState } from 'react';
import type { ChangeEvent, CSSProperties, FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import { CorpsIcon } from '../components/CorpsIcon';
import type { Corps, Season } from '../types/api';

type Tab = 'seasons' | 'corps';

const inputStyle: CSSProperties = {
  padding: '7px 10px', borderRadius: 5,
  background: 'var(--bg)', border: '1px solid var(--border-input)',
  color: 'var(--text-heading)', fontSize: 11, outline: 'none',
};

const primaryBtn: CSSProperties = {
  padding: '7px 14px', borderRadius: 5, fontSize: 11, fontWeight: 800,
  background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer',
  letterSpacing: '0.5px',
};

const disabledBtn: CSSProperties = {
  ...primaryBtn,
  background: 'var(--border)', color: 'var(--text-faint)', cursor: 'not-allowed',
};

const tabs: { key: Tab; label: string }[] = [
  { key: 'seasons', label: 'Seasons' },
  { key: 'corps', label: 'Corps' },
];

function SeasonBadge({ season }: { season: Season }) {
  if (season.isPublished) {
    return <span style={{ fontSize: 8, padding: '2px 6px', borderRadius: 4, fontWeight: 700, background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)' }}>PUBLISHED</span>;
  }

  if (season.status === 'Active') {
    return <span style={{ fontSize: 8, padding: '2px 6px', borderRadius: 4, fontWeight: 700, background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)' }}>ACTIVE</span>;
  }

  if (season.status === 'Completed') {
    return <span style={{ fontSize: 8, padding: '2px 6px', borderRadius: 4, fontWeight: 600, background: 'var(--surface)', color: 'var(--text-faint)', border: '1px solid var(--border)' }}>COMPLETED</span>;
  }

  return <span style={{ fontSize: 8, padding: '2px 6px', borderRadius: 4, fontWeight: 600, border: '1px solid var(--border)', color: 'var(--text-muted)' }}>UPCOMING</span>;
}

export function Admin() {
  const [tab, setTab] = useState<Tab>('seasons');
  const [seasons, setSeasons] = useState<Season[]>([]);
  const [newYear, setNewYear] = useState('');
  const [newStartDate, setNewStartDate] = useState('');
  const [newEndDate, setNewEndDate] = useState('');
  const [addingSeason, setAddingSeason] = useState(false);
  const [corps, setCorps] = useState<Corps[]>([]);
  const [newCorpsName, setNewCorpsName] = useState('');
  const [addingCorps, setAddingCorps] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editingCorpsId, setEditingCorpsId] = useState<string | null>(null);
  const [editingCorpsName, setEditingCorpsName] = useState('');
  const [savingCorpsEdit, setSavingCorpsEdit] = useState(false);
  const [deletingCorpsId, setDeletingCorpsId] = useState<string | null>(null);
  const [uploadingIconId, setUploadingIconId] = useState<string | null>(null);
  const iconInputRefs = useRef<Record<string, HTMLInputElement | null>>({});

  useEffect(() => {
    let cancelled = false;

    if (tab === 'seasons') {
      api.adminGetSeasons()
        .then(data => { if (!cancelled) { setError(null); setSeasons(data); } })
        .catch(() => { if (!cancelled) setError('Failed to load seasons.'); });
    } else {
      api.adminGetCorps()
        .then(data => { if (!cancelled) { setError(null); setCorps(data); } })
        .catch(() => { if (!cancelled) setError('Failed to load corps.'); });
    }

    return () => { cancelled = true; };
  }, [tab]);

  const addSeason = async (e: FormEvent) => {
    e.preventDefault();
    if (addingSeason) return;
    setAddingSeason(true);
    setError(null);

    try {
      await api.adminCreateSeason(Number(newYear), newStartDate, newEndDate);
      const updated = await api.adminGetSeasons();
      setSeasons(updated);
      setNewYear('');
      setNewStartDate('');
      setNewEndDate('');
    } catch {
      setError('Failed to add season.');
    } finally {
      setAddingSeason(false);
    }
  };

  const addCorps = async (e: FormEvent) => {
    e.preventDefault();
    if (addingCorps) return;
    setAddingCorps(true);
    setError(null);

    try {
      await api.adminCreateCorps(newCorpsName);
      const updated = await api.adminGetCorps();
      setCorps(updated);
      setNewCorpsName('');
    } catch {
      setError('Failed to add corps.');
    } finally {
      setAddingCorps(false);
    }
  };

  const saveCorpsRename = async (id: string) => {
    if (savingCorpsEdit) return;
    setSavingCorpsEdit(true);
    setError(null);

    try {
      await api.adminRenameCorps(id, editingCorpsName);
      const updated = await api.adminGetCorps();
      setCorps(updated);
      setEditingCorpsId(null);
    } catch {
      setError('Failed to rename corps.');
    } finally {
      setSavingCorpsEdit(false);
    }
  };

  const deleteCorps = async (id: string) => {
    if (deletingCorpsId) return;
    setDeletingCorpsId(id);
    setError(null);

    try {
      await api.adminDeleteCorps(id);
      const updated = await api.adminGetCorps();
      setCorps(updated);
    } catch {
      setError('Cannot delete: corps belongs to a published season.');
    } finally {
      setDeletingCorpsId(null);
    }
  };

  const triggerIconUpload = (id: string) => {
    iconInputRefs.current[id]?.click();
  };

  const handleIconFileChange = async (id: string, e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    e.target.value = '';

    if (!file || uploadingIconId) return;

    setUploadingIconId(id);
    setError(null);

    try {
      await api.adminUploadCorpsIcon(id, file);
      const updated = await api.adminGetCorps();
      setCorps(updated);
    } catch {
      setError('Failed to upload icon.');
    } finally {
      setUploadingIconId(null);
    }
  };

  return (
    <div>
      <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)', marginBottom: 20 }}>Admin</h2>

      <div style={{
        display: 'flex',
        background: 'var(--surface)',
        border: '1px solid var(--border)',
        borderRadius: '6px 6px 0 0',
      }}>
        {tabs.map(t => (
          <button
            key={t.key}
            onClick={() => setTab(t.key)}
            style={{
              flex: 1, padding: '10px 0', fontSize: 11, fontWeight: 600,
              cursor: 'pointer', background: 'transparent', border: 'none',
              color: tab === t.key ? 'var(--accent)' : 'var(--text-muted)',
              borderBottom: tab === t.key ? '2px solid var(--accent)' : '2px solid transparent',
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      <div style={{
        background: 'var(--surface-2)',
        border: '1px solid var(--border)',
        borderTop: 'none',
        borderBottomLeftRadius: 6,
        borderBottomRightRadius: 6,
        padding: 20,
      }}>
        {error && <div style={{ fontSize: 10, color: 'var(--red)', marginBottom: 12 }}>{error}</div>}

        {tab === 'seasons' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              {seasons.length === 0 && (
                <div style={{ fontSize: 11, color: 'var(--text-muted)', padding: '12px 0' }}>No seasons yet.</div>
              )}
              {seasons.map(s => (
                <div key={s.id} style={{
                  display: 'flex', alignItems: 'center', gap: 12,
                  padding: '10px 14px', background: 'var(--surface)',
                  border: '1px solid var(--border)', borderRadius: 5,
                }}>
                  <span style={{ fontSize: 13, fontWeight: 800, color: 'var(--text-heading)', minWidth: 40 }}>{s.year}</span>
                  <span style={{ fontSize: 10, color: 'var(--text-muted)', flex: 1 }}>{s.startDate} – {s.endDate}</span>
                  <SeasonBadge season={s} />
                  <Link to={`/admin/seasons/${s.id}`} style={{ fontSize: 10, color: 'var(--accent)', textDecoration: 'none', fontWeight: 600 }}>
                    Manage →
                  </Link>
                </div>
              ))}
            </div>

            <div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, padding: 16 }}>
              <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 12 }}>Add Season</div>
              <form onSubmit={addSeason} style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'flex-end' }}>
                <input type="number" value={newYear} onChange={e => setNewYear(e.target.value)} placeholder="Year" required style={{ ...inputStyle, width: 80 }} />
                <input type="date" value={newStartDate} onChange={e => setNewStartDate(e.target.value)} required style={{ ...inputStyle, width: 140 }} />
                <input type="date" value={newEndDate} onChange={e => setNewEndDate(e.target.value)} required style={{ ...inputStyle, width: 140 }} />
                <button type="submit" disabled={addingSeason} style={addingSeason ? disabledBtn : primaryBtn}>
                  Add Season
                </button>
              </form>
            </div>
          </div>
        )}

        {tab === 'corps' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
              {corps.length === 0 && (
                <div style={{ fontSize: 11, color: 'var(--text-muted)', padding: '12px 0' }}>No corps yet.</div>
              )}
              {corps.map(c => (
                <div key={c.id} style={{
                  display: 'flex', alignItems: 'center', gap: 8,
                  padding: '7px 14px', background: 'var(--surface)',
                  border: '1px solid var(--border)', borderRadius: 5,
                }}>
                  <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={28} />
                  {editingCorpsId === c.id ? (
                    <>
                      <input
                        value={editingCorpsName}
                        onChange={e => setEditingCorpsName(e.target.value)}
                        onKeyDown={e => {
                          if (e.key === 'Enter') { saveCorpsRename(c.id); }
                          if (e.key === 'Escape') { setEditingCorpsId(null); }
                        }}
                        autoFocus
                        style={{ ...inputStyle, flex: 1 }}
                      />
                      <button
                        onClick={() => saveCorpsRename(c.id)}
                        disabled={savingCorpsEdit || !editingCorpsName.trim()}
                        style={savingCorpsEdit ? disabledBtn : primaryBtn}
                      >
                        Save
                      </button>
                      <button
                        onClick={() => setEditingCorpsId(null)}
                        style={{ ...primaryBtn, background: 'transparent', color: 'var(--text-muted)', border: '1px solid var(--border)' }}
                      >
                        Cancel
                      </button>
                    </>
                  ) : (
                    <>
                      <span style={{ fontSize: 11, color: 'var(--text-heading)', flex: 1 }}>{c.name}</span>
                      <button
                        onClick={() => triggerIconUpload(c.id)}
                        disabled={uploadingIconId === c.id}
                        style={{
                          fontSize: 9, background: 'transparent', border: '1px solid var(--border)',
                          color: 'var(--text-muted)', cursor: uploadingIconId === c.id ? 'not-allowed' : 'pointer',
                          padding: '3px 8px', borderRadius: 3, opacity: uploadingIconId === c.id ? 0.5 : 1,
                        }}
                      >
                        {uploadingIconId === c.id ? 'Uploading…' : c.iconUrl ? 'Replace Icon' : 'Upload Icon'}
                      </button>
                      <button
                        onClick={() => { setEditingCorpsId(c.id); setEditingCorpsName(c.name); }}
                        style={{ fontSize: 10, background: 'transparent', border: 'none', color: 'var(--text-muted)', cursor: 'pointer', padding: '4px 8px' }}
                      >
                        Rename
                      </button>
                      <button
                        onClick={() => deleteCorps(c.id)}
                        disabled={!!deletingCorpsId}
                        style={{ fontSize: 10, background: 'transparent', border: 'none', color: 'var(--red)', cursor: 'pointer', padding: '4px 8px', opacity: deletingCorpsId === c.id ? 0.5 : 1 }}
                      >
                        Delete
                      </button>
                    </>
                  )}
                  <input
                    ref={el => { iconInputRefs.current[c.id] = el; }}
                    type="file"
                    accept="image/png,image/jpeg,image/webp,image/svg+xml"
                    style={{ display: 'none' }}
                    onChange={e => handleIconFileChange(c.id, e)}
                  />
                </div>
              ))}
            </div>

            <div style={{ background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, padding: 16 }}>
              <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 12 }}>Add Corps</div>
              <form onSubmit={addCorps} style={{ display: 'flex', gap: 8, alignItems: 'flex-end' }}>
                <input value={newCorpsName} onChange={e => setNewCorpsName(e.target.value)} placeholder="Corps name" required style={{ ...inputStyle, flex: 1 }} />
                <button type="submit" disabled={addingCorps} style={addingCorps ? disabledBtn : primaryBtn}>
                  Add Corps
                </button>
              </form>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
