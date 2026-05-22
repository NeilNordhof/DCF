import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';

type CaptionPreset = { label: string; description: string; captions: string[] };

const GE_PRESETS: CaptionPreset[] = [
  { label: 'Combined', description: 'General Effect (single score)',   captions: ['GeneralEffect'] },
  { label: 'Split',    description: 'GE1 Music · GE2 Visual',         captions: ['GeneralEffectMusic', 'GeneralEffectVisual'] },
];

const VISUAL_PRESETS: CaptionPreset[] = [
  { label: 'Combined',               description: 'Visual (single score)',                    captions: ['Visual'] },
  { label: 'Vis Perf + Color Guard', description: 'Visual Performance (VA+VP) · Color Guard', captions: ['VisualPerformance', 'ColorGuard'] },
  { label: 'Split',                  description: 'Vis Analysis · Vis Prof · Color Guard',    captions: ['VisualAnalysis', 'VisualProficiency', 'ColorGuard'] },
];

const MUSIC_PRESETS: CaptionPreset[] = [
  { label: 'Combined',           description: 'Music (single score)',                captions: ['Music'] },
  { label: 'Brass + Percussion', description: 'Brass · Percussion',                 captions: ['Brass', 'Percussion'] },
  { label: 'Split',              description: 'Brass · Music Analysis · Percussion', captions: ['Brass', 'MusicAnalysis', 'Percussion'] },
];

function PresetGroup({
  legend,
  name,
  presets,
  selected,
  onChange,
}: {
  legend: string;
  name: string;
  presets: CaptionPreset[];
  selected: number;
  onChange: (index: number) => void;
}) {
  return (
    <fieldset>
      <legend>{legend}</legend>
      {presets.map((preset, i) => {
        const id = `${name}-${i}`;

        return (
          <label key={preset.label} htmlFor={id}>
            <input
              type="radio"
              id={id}
              name={name}
              value={String(i)}
              checked={selected === i}
              onChange={() => onChange(i)}
            />
            <strong>{preset.label}</strong>
            {' — '}
            <span>{preset.description}</span>
          </label>
        );
      })}
    </fieldset>
  );
}

export function LeagueCreate() {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [isPublic, setIsPublic] = useState(true);
  const [corpsPerCaption, setCorpsPerCaption] = useState(3);
  const [gePreset, setGePreset] = useState(0);
  const [visualPreset, setVisualPreset] = useState(0);
  const [musicPreset, setMusicPreset] = useState(0);
  const [draftStartTime, setDraftStartTime] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      const league = await api.createLeague({
        name,
        isPublic,
        corpsPerCaption,
        draftableCaptions: [
          ...GE_PRESETS[gePreset].captions,
          ...VISUAL_PRESETS[visualPreset].captions,
          ...MUSIC_PRESETS[musicPreset].captions,
        ],
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
      <PresetGroup legend="General Effect" name="general-effect" presets={GE_PRESETS} selected={gePreset} onChange={setGePreset} />
      <PresetGroup legend="Visual" name="visual" presets={VISUAL_PRESETS} selected={visualPreset} onChange={setVisualPreset} />
      <PresetGroup legend="Music" name="music" presets={MUSIC_PRESETS} selected={musicPreset} onChange={setMusicPreset} />
      <label>Draft Start Time (optional): <input type="datetime-local" value={draftStartTime} onChange={e => setDraftStartTime(e.target.value)} /></label>
      {error && <p style={{ color: 'red' }}>{error}</p>}
      <button type="submit" disabled={submitting}>Create</button>
    </form>
  );
}
