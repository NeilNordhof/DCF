import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { Corps } from '../types/api';

export function Admin() {
  const [corps, setCorps] = useState<Corps[]>([]);
  const [newCorpsName, setNewCorpsName] = useState('');
  const [scrapeShowId, setScrapeShowId] = useState('');
  const [message, setMessage] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [addingCorps, setAddingCorps] = useState(false);
  const [scraping, setScraping] = useState(false);

  useEffect(() => {
    api.adminGetCorps().then(setCorps).catch(() => setError('Failed to load corps.'));
  }, []);

  const addCorps = async (e: React.FormEvent) => {
    e.preventDefault();
    if (addingCorps) return;
    setAddingCorps(true);
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

  const triggerScrape = async (e: React.FormEvent) => {
    e.preventDefault();
    if (scraping) return;
    setScraping(true);
    try {
      await api.adminTriggerScrape(scrapeShowId);
      setMessage(`Scrape triggered for show ${scrapeShowId}`);
    } catch {
      setMessage('Failed to trigger scrape.');
    } finally {
      setScraping(false);
    }
  };

  return (
    <div>
      <h2>Admin Panel</h2>
      {error && <p style={{ color: 'red' }}>{error}</p>}

      <section>
        <h3>Corps</h3>
        <ul>{corps.map(c => <li key={c.id}>{c.name}</li>)}</ul>
        <form onSubmit={addCorps}>
          <input value={newCorpsName} onChange={e => setNewCorpsName(e.target.value)} placeholder="Corps name" required />
          <button type="submit" disabled={addingCorps}>Add Corps</button>
        </form>
      </section>

      <section>
        <h3>Manual Scrape</h3>
        <form onSubmit={triggerScrape}>
          <input value={scrapeShowId} onChange={e => setScrapeShowId(e.target.value)} placeholder="Show ID" required />
          <button type="submit" disabled={scraping}>Trigger Scrape</button>
        </form>
        {message && <p>{message}</p>}
      </section>
    </div>
  );
}
