import { useEffect, useState } from 'react';
import type { CSSProperties } from 'react';
import { Link, useNavigate, useBlocker } from 'react-router-dom';
import { api } from '../api/client';
import type { ComputedCaption } from '../types/api';

type GEOption = 'combined' | 'split';
type VisOption = 'combined' | 'partial' | 'full';
type MusicOption = 'combined' | 'partial' | 'full';

function expandCaptions(ge: GEOption, vis: VisOption, music: MusicOption): ComputedCaption[] {
  const result: ComputedCaption[] = [];

  if (ge === 'combined') {
    result.push('GeneralEffectCombined');
  } else {
    result.push('GeneralEffect1', 'GeneralEffect2');
  }

  if (vis === 'combined') {
    result.push('VisualCombined');
  } else if (vis === 'partial') {
    result.push('Visual', 'Colorguard');
  } else {
    result.push('VisualAnalysis', 'VisualProficiency', 'Colorguard');
  }

  if (music === 'combined') {
    result.push('MusicCombined');
  } else if (music === 'partial') {
    result.push('Brass', 'Percussion');
  } else {
    result.push('Brass', 'MusicAnalysis', 'Percussion');
  }

  return result;
}

function Stepper({
  label,
  value,
  min,
  max,
  onChange,
  tooltip,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  onChange: (v: number) => void;
  tooltip: string;
}) {
  return (
    <div>
      <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>
        {label}
        {' '}
        <span title={tooltip} style={{ cursor: 'help', color: 'var(--text-muted)', fontSize: 9 }}>ⓘ</span>
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <button
          type="button"
          onClick={() => onChange(Math.max(min, value - 1))}
          disabled={value <= min}
          style={{
            width: 32, height: 32, borderRadius: 5, fontSize: 16, fontWeight: 700,
            background: 'var(--surface)', border: '1px solid var(--border)',
            color: value <= min ? 'var(--text-faint)' : 'var(--text-heading)',
            cursor: value <= min ? 'not-allowed' : 'pointer',
            opacity: value <= min ? 0.3 : 1,
          }}
        >
          −
        </button>
        <span style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)', minWidth: 24, textAlign: 'center' }}>
          {value}
        </span>
        <button
          type="button"
          onClick={() => onChange(Math.min(max, value + 1))}
          disabled={value >= max}
          style={{
            width: 32, height: 32, borderRadius: 5, fontSize: 16, fontWeight: 700,
            background: 'var(--surface)', border: '1px solid var(--border)',
            color: value >= max ? 'var(--text-faint)' : 'var(--text-heading)',
            cursor: value >= max ? 'not-allowed' : 'pointer',
            opacity: value >= max ? 0.3 : 1,
          }}
        >
          +
        </button>
      </div>
    </div>
  );
}

function datetimeLocalToIso(value: string): string {
  const offsetMinutes = new Date().getTimezoneOffset();
  const sign = offsetMinutes <= 0 ? '+' : '-';
  const abs = Math.abs(offsetMinutes);
  const hh = String(Math.floor(abs / 60)).padStart(2, '0');
  const mm = String(abs % 60).padStart(2, '0');
  return `${value}:00${sign}${hh}:${mm}`;
}

function DiscardDialog({ onKeep, onDiscard }: { onKeep: () => void; onDiscard: () => void }) {
  return (
    <div style={{
      position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.6)',
      display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100,
    }}>
      <div style={{
        background: 'var(--surface)', border: '1px solid var(--border)',
        borderRadius: 8, padding: 28, maxWidth: 340, width: '90%',
      }}>
        <h2 style={{ fontSize: 14, fontWeight: 800, color: 'var(--text-heading)', marginBottom: 10 }}>
          Discard changes?
        </h2>
        <p style={{ fontSize: 11, color: 'var(--text-muted)', marginBottom: 20 }}>
          Any unsaved changes will be lost.
        </p>
        <div style={{ display: 'flex', gap: 10 }}>
          <button
            type="button"
            onClick={onKeep}
            style={{
              flex: 1, padding: '8px 0', borderRadius: 5, fontSize: 11, fontWeight: 700,
              background: 'var(--surface)', border: '1px solid var(--border)',
              color: 'var(--text-heading)', cursor: 'pointer',
            }}
          >
            Keep editing
          </button>
          <button
            type="button"
            onClick={onDiscard}
            style={{
              flex: 1, padding: '8px 0', borderRadius: 5, fontSize: 11, fontWeight: 700,
              background: '#e06c75', border: 'none', color: '#fff', cursor: 'pointer',
            }}
          >
            Discard
          </button>
        </div>
      </div>
    </div>
  );
}

export function LeagueCreate() {
  const navigate = useNavigate();
  const [name, setName] = useState('');
  const [isPublic, setIsPublic] = useState(false);
  const [ge, setGe] = useState<GEOption>('combined');
  const [vis, setVis] = useState<VisOption>('combined');
  const [music, setMusic] = useState<MusicOption>('combined');
  const [corpsPerCaption, setCorpsPerCaption] = useState(3);
  const [maxPlayers, setMaxPlayers] = useState(8);
  const [draftStartTime, setDraftStartTime] = useState('');
  const [corpsCount, setCorpsCount] = useState<number | null>(null);
  const [seasonLoaded, setSeasonLoaded] = useState(false);
  const [hasActiveSeason, setHasActiveSeason] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [isDirty, setIsDirty] = useState(false);
  const [showDiscardForCancel, setShowDiscardForCancel] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.getActiveSeason()
      .then(s => { setCorpsCount(s.corpsCount); setMaxPlayers(Math.floor(s.corpsCount / 3)); setHasActiveSeason(true); })
      .catch(() => { setHasActiveSeason(false); })
      .finally(() => setSeasonLoaded(true));
  }, []);

  const maxCorpsPerCaption = corpsCount != null ? Math.floor(corpsCount / 4) : 99;
  const maxAllowedPlayers = corpsPerCaption > 0 && corpsCount != null
    ? Math.floor(corpsCount / corpsPerCaption)
    : 99;

  function handleCorpsPerCaptionChange(v: number) {
    setCorpsPerCaption(v);
    setIsDirty(true);

    if (corpsCount != null) {
      const newMax = Math.floor(corpsCount / v);
      setMaxPlayers(prev => Math.min(prev, newMax));
    }
  }

  const blocker = useBlocker(isDirty && !submitting);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setIsDirty(false);
    setError(null);

    try {
      const league = await api.createLeague({
        name,
        isPublic,
        corpsPerCaption,
        maxPlayers,
        draftableCaptions: expandCaptions(ge, vis, music),
        draftStartTime: draftStartTime ? datetimeLocalToIso(draftStartTime) : null,
      });

      navigate(`/leagues/${league.id}`);
    } catch (err) {
      setSubmitting(false);
      setIsDirty(true);
      setError('Failed to create league. Please try again.');
      console.error(err);
    }
  }

  function handleCancelClick() {
    if (isDirty) {
      setShowDiscardForCancel(true);
    } else {
      navigate('/leagues');
    }
  }

  const inputStyle: CSSProperties = {
    width: '100%', padding: '8px 10px', borderRadius: 5,
    background: 'var(--bg)', border: '1px solid var(--border-input)',
    color: 'var(--text-heading)', fontSize: 11, outline: 'none',
  };

  const selectStyle: CSSProperties = {
    width: '100%', padding: '7px 10px', borderRadius: 5,
    background: 'var(--surface)', border: '1px solid var(--border)',
    color: 'var(--text-heading)', fontSize: 11, outline: 'none',
  };

  const labelStyle: CSSProperties = {
    fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px',
    color: 'var(--text-faint)', marginBottom: 6,
  };

  if (!seasonLoaded) {
    return <div style={{ color: 'var(--text-muted)', fontSize: 11 }}>Loading…</div>;
  }

  if (!hasActiveSeason) {
    return (
      <div style={{
        background: 'var(--surface)', border: '1px solid var(--border)',
        borderRadius: 8, padding: 32, textAlign: 'center',
      }}>
        <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-heading)', marginBottom: 8 }}>
          No active season
        </div>
        <div style={{ fontSize: 11, color: 'var(--text-muted)' }}>
          Leagues can only be created during an active season.
        </div>
      </div>
    );
  }

  return (
    <div style={{ maxWidth: 480 }}>
      <Link to="/leagues" style={{ fontSize: 10, color: 'var(--text-muted)', textDecoration: 'none', display: 'block', marginBottom: 12 }}>
        ← Back to Leagues
      </Link>
      <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)', marginBottom: 24 }}>Create League</h2>

      <form onSubmit={handleSubmit} onChange={() => setIsDirty(true)} style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
        <div>
          <div style={labelStyle}>League Name</div>
          <input
            value={name}
            onChange={e => { setName(e.target.value); setIsDirty(true); }}
            required
            placeholder="My Fantasy League"
            style={inputStyle}
          />
        </div>

        <div>
          <div style={labelStyle}>Visibility</div>
          <div style={{ display: 'flex', gap: 4 }}>
            {([false, true] as const).map(pub => (
              <button
                key={String(pub)}
                type="button"
                onClick={() => { setIsPublic(pub); setIsDirty(true); }}
                style={{
                  flex: 1, padding: '7px 0', borderRadius: 5, fontSize: 11,
                  fontWeight: isPublic === pub ? 700 : 500, cursor: 'pointer',
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
          <div style={labelStyle}>Captions</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <div>
              <div style={{ fontSize: 9, color: 'var(--text-faint)', marginBottom: 4 }}>General Effect</div>
              <select
                style={selectStyle}
                value={ge}
                onChange={e => { setGe(e.target.value as GEOption); setIsDirty(true); }}
              >
                <option value="combined">Combined (GE Combined)</option>
                <option value="split">Split (GE1 Music + GE2 Visual)</option>
              </select>
            </div>
            <div>
              <div style={{ fontSize: 9, color: 'var(--text-faint)', marginBottom: 4 }}>Visual</div>
              <select
                style={selectStyle}
                value={vis}
                onChange={e => { setVis(e.target.value as VisOption); setIsDirty(true); }}
              >
                <option value="combined">Combined (Visual Combined)</option>
                <option value="partial">Partial Split (VA+VP combined, CG separate)</option>
                <option value="full">Full Split (VA, VP, and CG all separate)</option>
              </select>
            </div>
            <div>
              <div style={{ fontSize: 9, color: 'var(--text-faint)', marginBottom: 4 }}>Music</div>
              <select
                style={selectStyle}
                value={music}
                onChange={e => { setMusic(e.target.value as MusicOption); setIsDirty(true); }}
              >
                <option value="combined">Combined (Music Combined)</option>
                <option value="partial">Partial Split (Brass + Percussion)</option>
                <option value="full">Full Split (Brass + Music Analysis + Percussion)</option>
              </select>
            </div>
          </div>
        </div>

        <Stepper
          label="Corps per Caption"
          value={corpsPerCaption}
          min={1}
          max={maxCorpsPerCaption}
          onChange={handleCorpsPerCaptionChange}
          tooltip={`How many corps each player drafts per caption. Maximum is ${maxCorpsPerCaption} (1/4 of active season corps).`}
        />

        <Stepper
          label="Max Players"
          value={maxPlayers}
          min={4}
          max={maxAllowedPlayers}
          onChange={v => { setMaxPlayers(v); setIsDirty(true); }}
          tooltip={`Maximum league members. Capped at ${maxAllowedPlayers} so every player can draft a unique set of corps.`}
        />

        <div>
          <div style={labelStyle}>
            Draft Start <span style={{ textTransform: 'none', fontWeight: 400 }}>(optional)</span>
          </div>
          <input
            type="datetime-local"
            value={draftStartTime}
            onChange={e => { setDraftStartTime(e.target.value); setIsDirty(true); }}
            style={inputStyle}
          />
        </div>

        {error && <div style={{ fontSize: 10, color: '#e06c75' }}>{error}</div>}

        <div style={{ display: 'flex', gap: 10 }}>
          <button
            type="submit"
            disabled={submitting}
            style={{
              padding: '10px 28px', borderRadius: 5, fontSize: 11, fontWeight: 800,
              letterSpacing: '0.5px', textTransform: 'uppercase',
              background: submitting ? 'var(--border)' : 'var(--accent)',
              color: submitting ? 'var(--text-faint)' : 'var(--bg)',
              border: 'none', cursor: submitting ? 'not-allowed' : 'pointer',
            }}
          >
            {submitting ? 'Creating…' : 'Create League'}
          </button>
          <button
            type="button"
            onClick={handleCancelClick}
            style={{
              padding: '10px 20px', borderRadius: 5, fontSize: 11, fontWeight: 600,
              background: 'var(--surface)', border: '1px solid var(--border)',
              color: 'var(--text-muted)', cursor: 'pointer',
            }}
          >
            Cancel
          </button>
        </div>
      </form>

      {showDiscardForCancel && (
        <DiscardDialog
          onKeep={() => setShowDiscardForCancel(false)}
          onDiscard={() => navigate('/leagues')}
        />
      )}

      {blocker.state === 'blocked' && (
        <DiscardDialog
          onKeep={() => blocker.reset()}
          onDiscard={() => blocker.proceed()}
        />
      )}
    </div>
  );
}
