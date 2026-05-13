import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';

const ALL_CAPTIONS = [
  'GeneralEffect', 'Visual', 'ColorGuard', 'Brass', 'Percussion', 'Music'
];

export function LeagueCreate() {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [isPublic, setIsPublic] = useState(true);
  const [corpsPerCaption, setCorpsPerCaption] = useState(3);
  const [captions, setCaptions] = useState<string[]>(['GeneralEffect', 'Visual', 'ColorGuard', 'Brass', 'Percussion', 'Music']);
  const [draftStartTime, setDraftStartTime] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggle = (caption: string) =>
    setCaptions(prev =>
      prev.includes(caption) ? prev.filter(c => c !== caption) : [...prev, caption]
    );

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const league = await api.createLeague({
        name, isPublic, corpsPerCaption,
        draftableCaptions: captions,
        draftStartTime: draftStartTime || null,
      });
      navigate(`/leagues/${league.id}`);
    } catch {
      setError('Failed to create league. Please try again.');
      setSubmitting(false);
    }
  };

  return (
    <form onSubmit={submit}>
      <h2>Create League</h2>
      <label>Name: <input value={name} onChange={e => setName(e.target.value)} required /></label>
      <label>Public: <input type="checkbox" checked={isPublic} onChange={e => setIsPublic(e.target.checked)} /></label>
      <label>Corps per caption: <input type="number" value={corpsPerCaption} min={1} max={10} onChange={e => setCorpsPerCaption(Number(e.target.value))} /></label>
      <fieldset>
        <legend>Draftable Captions</legend>
        {ALL_CAPTIONS.map(c => (
          <label key={c}>
            <input type="checkbox" checked={captions.includes(c)} onChange={() => toggle(c)} /> {c}
          </label>
        ))}
      </fieldset>
      <label>Draft Start Time (optional): <input type="datetime-local" value={draftStartTime} onChange={e => setDraftStartTime(e.target.value)} /></label>
      {error && <p style={{ color: 'red' }}>{error}</p>}
      <button type="submit" disabled={submitting}>Create</button>
    </form>
  );
}
