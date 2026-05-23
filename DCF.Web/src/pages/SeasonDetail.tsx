import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { Corps, SeasonDetail as SeasonDetailType, Show } from '../types/api';

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
    Promise.all([
      api.adminGetSeason(id),
      api.adminGetCorps(),
      api.adminGetShows(id),
    ]).then(([s, corps, sh]) => {
      setSeason(s);
      setAllCorps(corps);
      setShows(sh);
      setSelectedCorpsIds(new Set(s.corpsIds));
    }).catch(() => setError('Failed to load season.'));
  }, [id]);

  const toggleCorps = (corpsId: string) => {
    setSelectedCorpsIds(prev => {
      const next = new Set(prev);
      if (next.has(corpsId)) {
        next.delete(corpsId);
      } else {
        next.add(corpsId);
      }
      return next;
    });
  };

  const saveCorps = async (e: React.FormEvent) => {
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
      if (next.has(corpsId)) {
        next.delete(corpsId);
      } else {
        next.add(corpsId);
      }
      return next;
    });
  };

  const addShow = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!id || addingShow) return;
    setAddingShow(true);
    setError(null);
    try {
      await api.adminCreateShow(
        id, showName, showUrl, showDate,
        new Date(showScoresTime).toISOString(),
        Array.from(showCorpsIds)
      );
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
    return <div>{error ?? 'Loading...'}</div>;
  }

  const seasonCorps = allCorps.filter(c => season.corpsIds.includes(c.id));

  return (
    <div>
      <h2>Season {season.year}</h2>
      <p>{season.startDate} — {season.endDate}</p>
      <p>Status: {season.status}</p>
      {season.isPublished && <p>Published</p>}
      {!season.isPublished && (
        <button
          onClick={publish}
          disabled={publishing || season.corpsIds.length === 0}
        >
          {publishing ? 'Publishing...' : 'Publish'}
        </button>
      )}
      {error && <p style={{ color: 'red' }}>{error}</p>}

      <section>
        <h3>Corps</h3>
        <form onSubmit={saveCorps}>
          {allCorps.map(c => (
            <label key={c.id} style={{ display: 'block' }}>
              <input
                type="checkbox"
                checked={selectedCorpsIds.has(c.id)}
                onChange={() => toggleCorps(c.id)}
                disabled={season.isPublished}
              />
              {c.name}
            </label>
          ))}
          <button type="submit" disabled={savingCorps || season.isPublished}>
            Save Corps
          </button>
        </form>
      </section>

      <section>
        <h3>Shows</h3>
        <table>
          <thead>
            <tr><th>Name</th><th>Date</th><th>URL</th></tr>
          </thead>
          <tbody>
            {shows.map(s => (
              <tr key={s.id}>
                <td>{s.name}</td>
                <td>{s.date}</td>
                <td>{s.url}</td>
              </tr>
            ))}
          </tbody>
        </table>

        <form onSubmit={addShow}>
          <input
            value={showName}
            onChange={e => setShowName(e.target.value)}
            placeholder="Show name"
            required
          />
          <input
            value={showUrl}
            onChange={e => setShowUrl(e.target.value)}
            placeholder="URL"
            required
          />
          <input
            type="date"
            value={showDate}
            onChange={e => setShowDate(e.target.value)}
            required
          />
          <input
            type="datetime-local"
            value={showScoresTime}
            onChange={e => setShowScoresTime(e.target.value)}
            required
          />
          <fieldset>
            <legend>Participating corps</legend>
            {seasonCorps.map(c => (
              <label key={c.id} style={{ display: 'block' }}>
                <input
                  type="checkbox"
                  checked={showCorpsIds.has(c.id)}
                  onChange={() => toggleShowCorps(c.id)}
                />
                {c.name}
              </label>
            ))}
          </fieldset>
          <button type="submit" disabled={addingShow}>Add Show</button>
        </form>
      </section>
    </div>
  );
}
