import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api/client';
import type { Corps, Season } from '../types/api';

type Tab = 'seasons' | 'corps';

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

  useEffect(() => {
    setError(null);
    if (tab === 'seasons') {
      api.adminGetSeasons().then(setSeasons).catch(() => setError('Failed to load seasons.'));
    } else {
      api.adminGetCorps().then(setCorps).catch(() => setError('Failed to load corps.'));
    }
  }, [tab]);

  const addSeason = async (e: React.FormEvent) => {
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

  const addCorps = async (e: React.FormEvent) => {
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

  return (
    <div>
      <h2>Admin Panel</h2>
      {error && <p style={{ color: 'red' }}>{error}</p>}

      <div>
        <button onClick={() => setTab('seasons')} disabled={tab === 'seasons'}>Seasons</button>
        <button onClick={() => setTab('corps')} disabled={tab === 'corps'}>Corps</button>
      </div>

      {tab === 'seasons' && (
        <section>
          <h3>Seasons</h3>
          <table>
            <thead>
              <tr>
                <th>Year</th>
                <th>Start</th>
                <th>End</th>
                <th>Status</th>
                <th>Published</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {seasons.map(s => (
                <tr key={s.id}>
                  <td>{s.year}</td>
                  <td>{s.startDate}</td>
                  <td>{s.endDate}</td>
                  <td>{s.status}</td>
                  <td>{s.isPublished ? 'Published' : ''}</td>
                  <td><Link to={`/admin/seasons/${s.id}`}>Manage →</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
          <form onSubmit={addSeason}>
            <input
              type="number"
              value={newYear}
              onChange={e => setNewYear(e.target.value)}
              placeholder="Year"
              required
            />
            <input
              type="date"
              value={newStartDate}
              onChange={e => setNewStartDate(e.target.value)}
              required
            />
            <input
              type="date"
              value={newEndDate}
              onChange={e => setNewEndDate(e.target.value)}
              required
            />
            <button type="submit" disabled={addingSeason}>Add Season</button>
          </form>
        </section>
      )}

      {tab === 'corps' && (
        <section>
          <h3>Corps</h3>
          <ul>{corps.map(c => <li key={c.id}>{c.name}</li>)}</ul>
          <form onSubmit={addCorps}>
            <input
              value={newCorpsName}
              onChange={e => setNewCorpsName(e.target.value)}
              placeholder="Corps name"
              required
            />
            <button type="submit" disabled={addingCorps}>Add Corps</button>
          </form>
        </section>
      )}
    </div>
  );
}
