import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';

const ALL_CAPTIONS: { value: string; label: string; group: string }[] = [
  { value: 'GeneralEffect',        label: 'GE',            group: 'General Effect' },
  { value: 'GeneralEffectMusic',   label: 'GE1 Music',     group: 'General Effect' },
  { value: 'GeneralEffectVisual',  label: 'GE2 Visual',    group: 'General Effect' },
  { value: 'Visual',               label: 'Visual',        group: 'Visual' },
  { value: 'VisualPerformance',    label: 'Vis Perf',      group: 'Visual' },
  { value: 'VisualAnalysis',       label: 'Vis Analysis',  group: 'Visual' },
  { value: 'VisualProficiency',    label: 'Vis Prof',      group: 'Visual' },
  { value: 'ColorGuard',           label: 'Color Guard',   group: 'Visual' },
  { value: 'Music',                label: 'Music',         group: 'Music' },
  { value: 'Brass',                label: 'Brass',         group: 'Music' },
  { value: 'MusicAnalysis',        label: 'Mus Analysis',  group: 'Music' },
  { value: 'Percussion',           label: 'Percussion',    group: 'Music' },
];

const CAPTION_GROUPS = ['General Effect', 'Visual', 'Music'];

function Chip({ label, selected, onClick }: { label: string; selected: boolean; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        padding: '5px 12px', borderRadius: 5, fontSize: 10, fontWeight: selected ? 600 : 500,
        cursor: 'pointer', border: `1px solid ${selected ? 'var(--accent)' : 'var(--border)'}`,
        background: selected ? 'var(--accent-bg)' : 'var(--surface)',
        color: selected ? 'var(--text-heading)' : 'var(--text-muted)',
      }}
    >
      {label}
    </button>
  );
}

export function LeagueCreate() {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [isPublic, setIsPublic] = useState(true);
  const [corpsPerCaption, setCorpsPerCaption] = useState(3);
  const [selectedCaptions, setSelectedCaptions] = useState<Set<string>>(new Set(['Brass', 'Percussion', 'ColorGuard', 'GeneralEffectMusic', 'GeneralEffectVisual', 'VisualAnalysis', 'VisualProficiency']));
  const [draftStartTime, setDraftStartTime] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const toggleCaption = (value: string) => {
    setSelectedCaptions(prev => {
      const next = new Set(prev);
      if (next.has(value)) {
        next.delete(value);
      } else {
        next.add(value);
      }
      return next;
    });
  };

  const totalPicks = corpsPerCaption * selectedCaptions.size;

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (selectedCaptions.size === 0) {
      setError('Select at least one caption.');
      return;
    }
    setSubmitting(true);
    setError(null);

    try {
      const league = await api.createLeague({
        name,
        isPublic,
        corpsPerCaption,
        draftableCaptions: Array.from(selectedCaptions),
        draftStartTime: draftStartTime || null,
      });

      navigate(`/leagues/${league.id}`);
    } catch {
      setError('Failed to create league. Please try again.');
      setSubmitting(false);
    }
  };

  const inputStyle: React.CSSProperties = {
    width: '100%', padding: '8px 10px', borderRadius: 5,
    background: 'var(--bg)', border: '1px solid var(--border-input)',
    color: 'var(--text-heading)', fontSize: 11, outline: 'none',
  };

  return (
    <div style={{ maxWidth: 480 }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 24 }}>
        <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)' }}>Create League</h2>
        <Link to="/leagues" style={{ fontSize: 10, color: 'var(--text-muted)', textDecoration: 'none' }}>← Back to Leagues</Link>
      </div>

      <form onSubmit={submit} style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>League Name</div>
          <input
            value={name}
            onChange={e => setName(e.target.value)}
            required
            placeholder="My Fantasy League"
            style={inputStyle}
          />
        </div>

        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>Visibility</div>
          <div style={{ display: 'flex', gap: 4 }}>
            {[true, false].map(pub => (
              <button
                key={String(pub)}
                type="button"
                onClick={() => setIsPublic(pub)}
                style={{
                  flex: 1, padding: '7px 0', borderRadius: 5, fontSize: 11, fontWeight: isPublic === pub ? 700 : 500,
                  cursor: 'pointer',
                  border: `1px solid ${isPublic === pub ? 'var(--accent)' : 'var(--border)'}`,
                  background: isPublic === pub ? 'var(--accent-bg)' : 'var(--surface)',
                  color: isPublic === pub ? 'var(--text-heading)' : 'var(--text-muted)',
                }}
              >
                {pub ? 'Public' : 'Private'}
              </button>
            ))}
          </div>
        </div>

        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 10 }}>Captions</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
            {CAPTION_GROUPS.map(group => (
              <div key={group}>
                <div style={{ fontSize: 8, color: 'var(--text-faint)', marginBottom: 6, letterSpacing: '0.3px' }}>{group}</div>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
                  {ALL_CAPTIONS.filter(c => c.group === group).map(c => (
                    <Chip
                      key={c.value}
                      label={c.label}
                      selected={selectedCaptions.has(c.value)}
                      onClick={() => toggleCaption(c.value)}
                    />
                  ))}
                </div>
              </div>
            ))}
          </div>
        </div>

        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>Corps per Caption</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <button
              type="button"
              onClick={() => setCorpsPerCaption(Math.max(1, corpsPerCaption - 1))}
              style={{
                width: 32, height: 32, borderRadius: 5, fontSize: 16, fontWeight: 700,
                background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text-heading)', cursor: 'pointer',
              }}
            >−</button>
            <span style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)', minWidth: 20, textAlign: 'center' }}>{corpsPerCaption}</span>
            <button
              type="button"
              onClick={() => setCorpsPerCaption(Math.min(10, corpsPerCaption + 1))}
              style={{
                width: 32, height: 32, borderRadius: 5, fontSize: 16, fontWeight: 700,
                background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text-heading)', cursor: 'pointer',
              }}
            >+</button>
            <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>= {totalPicks} total picks</span>
          </div>
        </div>

        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>
            Draft Start <span style={{ color: 'var(--text-faint)', fontWeight: 400, textTransform: 'none' }}>(optional)</span>
          </div>
          <input
            type="datetime-local"
            value={draftStartTime}
            onChange={e => setDraftStartTime(e.target.value)}
            style={inputStyle}
          />
        </div>

        {error && <div style={{ fontSize: 10, color: 'var(--red)' }}>{error}</div>}

        <button
          type="submit"
          disabled={submitting}
          style={{
            width: '100%', padding: '10px 0', borderRadius: 5, fontSize: 11, fontWeight: 800,
            letterSpacing: '0.5px', textTransform: 'uppercase',
            background: submitting ? 'var(--border)' : 'var(--accent)',
            color: submitting ? 'var(--text-faint)' : 'var(--bg)',
            border: 'none', cursor: submitting ? 'not-allowed' : 'pointer',
          }}
        >
          {submitting ? 'Creating…' : 'Create League'}
        </button>
      </form>
    </div>
  );
}
