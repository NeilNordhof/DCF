import { useEffect, useState } from 'react';
import { api } from '../api/client';
import type { Corps } from '../types/api';

export function Admin() {
  const [corps, setCorps] = useState<Corps[]>([]);
  const [newCorpsName, setNewCorpsName] = useState('');
  const [scrapeShowId, setScrapeShowId] = useState('');
  const [message, setMessage] = useState('');

  useEffect(() => { api.adminGetCorps().then(setCorps); }, []);

  const addCorps = async (e: React.FormEvent) => {
    e.preventDefault();
    await api.adminCreateCorps(newCorpsName);
    const updated = await api.adminGetCorps();
    setCorps(updated);
    setNewCorpsName('');
  };

  const triggerScrape = async (e: React.FormEvent) => {
    e.preventDefault();
    await api.adminTriggerScrape(scrapeShowId);
    setMessage(`Scrape triggered for show ${scrapeShowId}`);
  };

  return (
    <div>
      <h2>Admin Panel</h2>

      <section>
        <h3>Corps</h3>
        <ul>{corps.map(c => <li key={c.id}>{c.name}</li>)}</ul>
        <form onSubmit={addCorps}>
          <input value={newCorpsName} onChange={e => setNewCorpsName(e.target.value)} placeholder="Corps name" required />
          <button type="submit">Add Corps</button>
        </form>
      </section>

      <section>
        <h3>Manual Scrape</h3>
        <form onSubmit={triggerScrape}>
          <input value={scrapeShowId} onChange={e => setScrapeShowId(e.target.value)} placeholder="Show ID" required />
          <button type="submit">Trigger Scrape</button>
        </form>
        {message && <p>{message}</p>}
      </section>
    </div>
  );
}
