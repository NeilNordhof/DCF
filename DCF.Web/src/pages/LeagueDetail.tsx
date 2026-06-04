import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';
import { LeagueScoresTab } from '../components/LeagueScoresTab';
import { useMqtt } from '../mqtt/useMqtt';
import { useUser } from '../context/UserContext';
import type { ComputedCaption, DraftState, League, MemberScoreBreakdown, Standing } from '../types/api';

type Tab = 'home' | 'scores' | 'members' | 'picks' | 'info';
type GEOption = 'combined' | 'split';
type VisOption = 'combined' | 'partial' | 'full';
type MusicOption = 'combined' | 'partial' | 'full';

function expandCaptions(ge: GEOption, vis: VisOption, music: MusicOption): ComputedCaption[] {
  const result: ComputedCaption[] = [];

  if (ge === 'combined') {
    result.push('GeneralEffectCombined');
  }
  else {
    result.push('GeneralEffect1', 'GeneralEffect2');
  }

  if (vis === 'combined') {
    result.push('VisualCombined');
  }
  else if (vis === 'partial') {
    result.push('Visual', 'Colorguard');
  }
  else {
    result.push('VisualAnalysis', 'VisualProficiency', 'Colorguard');
  }

  if (music === 'combined') {
    result.push('MusicCombined');
  }
  else if (music === 'partial') {
    result.push('Brass', 'Percussion');
  }
  else {
    result.push('Brass', 'MusicAnalysis', 'Percussion');
  }

  return result;
}

function captionsToOptions(captions: ComputedCaption[]): { ge: GEOption; vis: VisOption; music: MusicOption } {
  const set = new Set(captions);
  const ge: GEOption = set.has('GeneralEffectCombined') ? 'combined' : 'split';
  let vis: VisOption = 'combined';

  if (set.has('VisualAnalysis') || set.has('VisualProficiency')) {
    vis = 'full';
  }
  else if (set.has('Visual')) {
    vis = 'partial';
  }

  let music: MusicOption = 'combined';

  if (set.has('MusicAnalysis')) {
    music = 'full';
  }
  else if (set.has('Brass') || set.has('Percussion')) {
    music = 'partial';
  }

  return { ge, vis, music };
}

function toDatetimeLocal(iso: string | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

function datetimeLocalToIso(value: string): string {
  const offsetMinutes = new Date().getTimezoneOffset();
  const sign = offsetMinutes <= 0 ? '+' : '-';
  const abs = Math.abs(offsetMinutes);
  const hh = String(Math.floor(abs / 60)).padStart(2, '0');
  const mm = String(abs % 60).padStart(2, '0');
  return `${value}:00${sign}${hh}:${mm}`;
}

export function LeagueDetail() {
  const { id } = useParams<{ id: string }>();
  const [searchParams] = useSearchParams();
  const code = searchParams.get('code') ?? undefined;
  const { user } = useUser();
  const navigate = useNavigate();
  const [league, setLeague] = useState<League | null>(null);
  const [standings, setStandings] = useState<Standing[]>([]);
  const [breakdown, setBreakdown] = useState<MemberScoreBreakdown[]>([]);
  const [activeTab, setActiveTab] = useState<Tab>('home');
  const [activePicksPlayer, setActivePicksPlayer] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [copying, setCopying] = useState(false);
  const [editing, setEditing] = useState(false);
  const [editGe, setEditGe] = useState<GEOption>('combined');
  const [editVis, setEditVis] = useState<VisOption>('combined');
  const [editMusic, setEditMusic] = useState<MusicOption>('combined');
  const [editCorpsPerCaption, setEditCorpsPerCaption] = useState(1);
  const [editDraftStartTime, setEditDraftStartTime] = useState('');
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [refreshingCode, setRefreshingCode] = useState(false);
  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);
  const scoresUpdated = useMqtt<{ showId: string }>('dcf/scores/updated');

  useEffect(() => {
    if (id) {
      api.getLeague(id, code).then(setLeague).catch(() => setError('Failed to load league.'));
    }
  }, [id]);

  useEffect(() => {
    if (id) {
      api.getStandings(id).then(setStandings).catch(() => {});
    }
  }, [id, scoresUpdated]);

  useEffect(() => {
    if (id && activeTab === 'scores') {
      api.getScoreBreakdown(id).then(setBreakdown).catch(() => {});
    }
  }, [id, activeTab, scoresUpdated]);

  if (error) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>{error}</div>;
  }

  if (!league) {
    return <div style={{ color: 'var(--text-muted)', padding: 16 }}>Loading…</div>;
  }

  const effectiveStatus = draftState?.status ?? league.draftStatus;

  const handleJoin = async () => {
    try {
      await api.joinLeague(league.id, code);
      window.location.reload();
      //navigate(`/leagues/${league.id}`);
    } catch {
      setError('Failed to join league. Check your invite code.');
    }
  };

  const openDraft = () => {
    if (id) api.openDraft(id).then(() => {navigate(`/leagues/${id}/draft`)}).catch(() => {});
  };

  const copyInviteCode = () => {
    if (league.inviteCode) {
      navigator.clipboard.writeText(league.inviteCode);
      setCopying(true);
      setTimeout(() => setCopying(false), 1500);
    }
  };

  const refreshInviteCode = async () => {
    setRefreshingCode(true);

    try {
      const result = await api.refreshInviteCode(id!);
      setLeague(prev => prev ? { ...prev, inviteCode: result.inviteCode } : prev);
    }
    catch {}
    finally {
      setRefreshingCode(false);
    }
  };

  const startEditing = () => {
    const { ge, vis, music } = captionsToOptions(league.draftableCaptions!);
    setEditGe(ge);
    setEditVis(vis);
    setEditMusic(music);
    setEditCorpsPerCaption(league.corpsPerCaption!);
    setEditDraftStartTime(toDatetimeLocal(league.draftStartTime));
    setSaveError(null);
    setEditing(true);
  };

  const saveEdits = async () => {
    setSaving(true);
    setSaveError(null);

    try {
      await api.updateLeague(id!, {
        corpsPerCaption: editCorpsPerCaption,
        draftableCaptions: expandCaptions(editGe, editVis, editMusic),
        draftStartTime: editDraftStartTime ? datetimeLocalToIso(editDraftStartTime) : null,
      });

      const updated = await api.getLeague(id!);
      setLeague(updated);
      setEditing(false);
    }
    catch (e) {
      setSaveError(e instanceof Error ? e.message : 'Failed to save');
    }
    finally {
      setSaving(false);
    }
  };

  const statusBadge = () => {
    const s = effectiveStatus;

    if (s === 'InProgress') {
      return (
        <span style={{
          fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 700,
          textTransform: 'uppercase', letterSpacing: '0.5px',
          background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)',
        }}>LIVE DRAFT</span>
      );
    }

    if (s === 'Open') {
      return (
        <span style={{
          fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 700,
          textTransform: 'uppercase', letterSpacing: '0.5px',
          background: 'var(--green-bg)', color: 'var(--green)', border: '1px solid var(--green-border)',
        }}>LOBBY OPEN</span>
      );
    }

    const label = s === 'Scheduled' ? 'SCHEDULED' : s === 'Completed' ? 'COMPLETED' : 'NOT STARTED';

    return (
      <span style={{
        fontSize: 8, padding: '2px 8px', borderRadius: 4, fontWeight: 600,
        textTransform: 'uppercase', letterSpacing: '0.5px',
        border: '1px solid var(--border)', color: 'var(--text-muted)',
      }}>{label}</span>
    );
  };

  const tabs: { key: Tab; label: string }[] = [
    { key: 'home', label: 'Home' },
    { key: 'scores', label: 'Scores' },
    { key: 'members', label: 'Members' },
    { key: 'picks', label: 'Picks' },
    { key: 'info', label: 'Info' },
  ];

  const renderHomeTab = () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {standings.length === 0 && (
        <div style={{ color: 'var(--text-muted)', fontSize: 11, padding: '20px 0' }}>
          {effectiveStatus === 'NotStarted' || effectiveStatus === 'Scheduled'
            ? 'Standings will appear once the draft begins.'
            : 'No standings yet.'}
        </div>
      )}
      {standings.map((s, i) => {
        const isMe = s.userId === user?.id;
        const rank = i + 1;

        return (
          <div
            key={s.userId}
            style={{
              display: 'flex', alignItems: 'center', gap: 12,
              padding: '10px 14px',
              background: isMe ? 'var(--accent-bg)' : 'var(--surface)',
              border: `1px solid ${isMe ? 'var(--accent-border)' : 'var(--border)'}`,
              borderRadius: 5,
            }}
          >
            <span style={{
              fontSize: 12, fontWeight: 800, minWidth: 20,
              color: rank === 1 ? 'var(--accent)' : 'var(--text-muted)',
            }}>
              {rank}
            </span>
            <span style={{ flex: 1, fontSize: 11, fontWeight: 600, color: 'var(--text-heading)' }}>{s.displayName}</span>
            <span style={{ fontSize: 12, fontWeight: 700, color: isMe ? 'var(--accent)' : 'var(--text-heading)' }}>
              {s.score.toFixed(3)}
            </span>
          </div>
        );
      })}
    </div>
  );

  const renderMembersTab = () => (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 2 }}>
      {(league.members ?? []).map(m => (
        <div
          key={m.userId}
          style={{
            display: 'flex', alignItems: 'center', justifyContent: 'space-between',
            padding: '9px 14px',
            background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5,
          }}
        >
          <span style={{ fontSize: 11, color: 'var(--text-heading)' }}>{m.displayName}</span>
          {m.userId === league.commissionerUserId && (
            <span style={{ fontSize: 8, color: 'var(--text-faint)', textTransform: 'uppercase', letterSpacing: '0.5px' }}>
              Commissioner
            </span>
          )}
        </div>
      ))}
    </div>
  );

  const renderPicksTab = () => {
    const members = league.members ?? [];
    const effectivePlayer = activePicksPlayer ?? members[0]?.userId ?? null;
    const currentPlayer = members.find(m => m.userId === effectivePlayer) ?? members[0];
    if (!currentPlayer) return <div style={{ color: 'var(--text-muted)', fontSize: 11 }}>No members yet.</div>;

    const allPicks = league.picks ?? [];
    const playerPicks = allPicks.filter(p => p.userId === currentPlayer.userId);

    return (
      <div>
        <div style={{ display: 'flex', gap: 4, marginBottom: 16, flexWrap: 'wrap' }}>
          {members.map(m => (
            <button
              key={m.userId}
              onClick={() => setActivePicksPlayer(m.userId)}
              style={{
                padding: '4px 12px', borderRadius: 12, fontSize: 10, fontWeight: 600,
                cursor: 'pointer', border: 'none',
                background: effectivePlayer === m.userId ? 'var(--accent)' : 'var(--surface)',
                color: effectivePlayer === m.userId ? 'var(--bg)' : 'var(--text-muted)',
              }}
            >
              {m.displayName.split(' ')[0]}
            </button>
          ))}
        </div>
        {league.draftableCaptions!.map(cap => {
          const capPicks = playerPicks.filter(p => p.caption === cap);
          const filled = capPicks.length;
          const total = league.corpsPerCaption!;

          return (
            <div key={cap} style={{ marginBottom: 12 }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 6 }}>
                <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>{cap}</div>
                <div style={{
                  fontSize: 8, padding: '1px 6px', borderRadius: 8, fontWeight: 700,
                  background: filled > 0 ? 'var(--accent-bg)' : 'var(--surface)',
                  color: filled > 0 ? 'var(--accent)' : 'var(--text-faint)',
                  border: `1px solid ${filled > 0 ? 'var(--accent-border)' : 'var(--border)'}`,
                }}>
                  {filled} / {total}
                </div>
              </div>
              {Array.from({ length: total }).map((_, i) => {
                const pick = capPicks[i];

                if (pick) {
                  return (
                    <div key={i} style={{
                      display: 'flex', alignItems: 'center', gap: 8,
                      padding: '6px 10px', background: 'var(--surface)',
                      border: '1px solid var(--border)', borderRadius: 5, marginBottom: 4,
                    }}>
                      <div style={{
                        width: 20, height: 20, borderRadius: '50%',
                        background: 'var(--accent-bg)', border: '1px solid var(--accent-border)',
                        display: 'flex', alignItems: 'center', justifyContent: 'center',
                        fontSize: 8, color: 'var(--accent)', flexShrink: 0,
                      }}>
                        #{pick.pickNumber + 1}
                      </div>
                      <div>
                        <div style={{ fontSize: 10, fontWeight: 600, color: 'var(--text-heading)' }}>{pick.corpsName}</div>
                        <div style={{ fontSize: 8, color: 'var(--text-muted)' }}>Pick #{pick.pickNumber + 1} overall</div>
                      </div>
                    </div>
                  );
                }

                return (
                  <div key={i} style={{ padding: '6px 10px', border: '1px dashed var(--border)', borderRadius: 5, marginBottom: 4 }}>
                    <span style={{ fontSize: 10, fontStyle: 'italic', color: 'var(--text-faint)' }}>Empty</span>
                  </div>
                );
              })}
            </div>
          );
        })}
      </div>
    );
  };

  const renderInfoTab = () => {
    const draftSettingsEditable = league.isCommissioner &&
      (effectiveStatus === 'NotStarted' || effectiveStatus === 'Scheduled');
    const inputStyle = {
      width: '100%', padding: '7px 10px', borderRadius: 5,
      background: 'var(--bg)', border: '1px solid var(--border)',
      color: 'var(--text-heading)', fontSize: 11, outline: 'none', boxSizing: 'border-box' as const,
    };
    const selectStyle = {
      width: '100%', padding: '7px 10px', borderRadius: 5,
      background: 'var(--surface)', border: '1px solid var(--border)',
      color: 'var(--text-heading)', fontSize: 11, outline: 'none',
    };
    const labelStyle = {
      fontSize: 8, textTransform: 'uppercase' as const, letterSpacing: '0.5px',
      color: 'var(--text-faint)', marginBottom: 6,
    };

    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
        {league.inviteCode && (
          <div>
            <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 8 }}>Invite Code</div>
            <div style={{
              display: 'flex', alignItems: 'center', gap: 10,
              padding: '10px 14px', background: 'var(--surface-2)',
              border: '1px solid var(--border)', borderRadius: 5,
            }}>
              <span style={{ fontFamily: 'var(--mono)', fontSize: 13, color: 'var(--accent)', flex: 1, letterSpacing: '0.5px' }}>
                {league.inviteCode}
              </span>
              <button
                onClick={copyInviteCode}
                style={{
                  fontSize: 10, fontWeight: 600, padding: '4px 10px', borderRadius: 4,
                  background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text-muted)',
                  cursor: 'pointer',
                }}
              >
                {copying ? 'Copied!' : 'Copy'}
              </button>
              {league.isCommissioner && (
                <button
                  onClick={refreshInviteCode}
                  disabled={refreshingCode}
                  style={{
                    fontSize: 10, fontWeight: 600, padding: '4px 10px', borderRadius: 4,
                    background: 'var(--surface)', border: '1px solid var(--border)',
                    color: refreshingCode ? 'var(--text-faint)' : 'var(--text-muted)',
                    cursor: refreshingCode ? 'not-allowed' : 'pointer',
                  }}
                >
                  {refreshingCode ? 'Refreshing…' : 'Refresh'}
                </button>
              )}
            </div>
          </div>
        )}
        <div>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 8 }}>
            <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Draft Settings</div>
            {draftSettingsEditable && !editing && (
              <button
                onClick={startEditing}
                style={{
                  fontSize: 10, fontWeight: 600, padding: '3px 10px', borderRadius: 4,
                  background: 'var(--surface)', border: '1px solid var(--border)',
                  color: 'var(--text-muted)', cursor: 'pointer',
                }}
              >
                Edit
              </button>
            )}
          </div>
          {!editing ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
              {[
                { label: 'Captions', value: league.draftableCaptions!.join(', ') },
                { label: 'Corps per Caption', value: String(league.corpsPerCaption) },
                { label: 'Draft Start', value: league.draftStartTime ? new Date(league.draftStartTime).toLocaleString() : 'Not scheduled' },
              ].map(item => (
                <div key={item.label} style={{
                  display: 'flex', justifyContent: 'space-between', alignItems: 'center',
                  padding: '8px 14px', background: 'var(--surface)',
                  border: '1px solid var(--border)', borderRadius: 5,
                }}>
                  <span style={{ fontSize: 10, color: 'var(--text-muted)' }}>{item.label}</span>
                  <span style={{ fontSize: 10, fontWeight: 600, color: 'var(--text-heading)' }}>{item.value}</span>
                </div>
              ))}
            </div>
          ) : (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
              <div>
                <div style={labelStyle}>Captions</div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                  <div>
                    <div style={{ fontSize: 9, color: 'var(--text-faint)', marginBottom: 4 }}>General Effect</div>
                    <select style={selectStyle} value={editGe} onChange={e => setEditGe(e.target.value as GEOption)}>
                      <option value="combined">Combined (GE Combined)</option>
                      <option value="split">Split (GE1 Music + GE2 Visual)</option>
                    </select>
                  </div>
                  <div>
                    <div style={{ fontSize: 9, color: 'var(--text-faint)', marginBottom: 4 }}>Visual</div>
                    <select style={selectStyle} value={editVis} onChange={e => setEditVis(e.target.value as VisOption)}>
                      <option value="combined">Combined (Visual Combined)</option>
                      <option value="partial">Partial Split (VA+VP combined, CG separate)</option>
                      <option value="full">Full Split (VA, VP, and CG all separate)</option>
                    </select>
                  </div>
                  <div>
                    <div style={{ fontSize: 9, color: 'var(--text-faint)', marginBottom: 4 }}>Music</div>
                    <select style={selectStyle} value={editMusic} onChange={e => setEditMusic(e.target.value as MusicOption)}>
                      <option value="combined">Combined (Music Combined)</option>
                      <option value="partial">Partial Split (Brass + Percussion)</option>
                      <option value="full">Full Split (Brass + Music Analysis + Percussion)</option>
                    </select>
                  </div>
                </div>
              </div>
              <div>
                <div style={labelStyle}>Corps per Caption</div>
                <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                  <button
                    type="button"
                    onClick={() => setEditCorpsPerCaption(v => Math.max(1, v - 1))}
                    disabled={editCorpsPerCaption <= 1}
                    style={{
                      width: 32, height: 32, borderRadius: 5, fontSize: 16, fontWeight: 700,
                      background: 'var(--surface)', border: '1px solid var(--border)',
                      color: editCorpsPerCaption <= 1 ? 'var(--text-faint)' : 'var(--text-heading)',
                      cursor: editCorpsPerCaption <= 1 ? 'not-allowed' : 'pointer',
                      opacity: editCorpsPerCaption <= 1 ? 0.3 : 1,
                    }}
                  >
                    −
                  </button>
                  <span style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)', minWidth: 24, textAlign: 'center' }}>
                    {editCorpsPerCaption}
                  </span>
                  <button
                    type="button"
                    onClick={() => setEditCorpsPerCaption(v => v + 1)}
                    style={{
                      width: 32, height: 32, borderRadius: 5, fontSize: 16, fontWeight: 700,
                      background: 'var(--surface)', border: '1px solid var(--border)',
                      color: 'var(--text-heading)', cursor: 'pointer',
                    }}
                  >
                    +
                  </button>
                </div>
              </div>
              <div>
                <div style={labelStyle}>
                  Draft Start <span style={{ textTransform: 'none', fontWeight: 400 }}>(leave blank to remove)</span>
                </div>
                <input
                  type="datetime-local"
                  value={editDraftStartTime}
                  onChange={e => setEditDraftStartTime(e.target.value)}
                  style={inputStyle}
                />
              </div>
              {saveError && (
                <div style={{ fontSize: 10, color: '#e06c75' }}>{saveError}</div>
              )}
              <div style={{ display: 'flex', gap: 8 }}>
                <button
                  onClick={saveEdits}
                  disabled={saving}
                  style={{
                    padding: '8px 20px', borderRadius: 5, fontSize: 11, fontWeight: 700,
                    background: saving ? 'var(--border)' : 'var(--accent)',
                    color: saving ? 'var(--text-faint)' : 'var(--bg)',
                    border: 'none', cursor: saving ? 'not-allowed' : 'pointer',
                  }}
                >
                  {saving ? 'Saving…' : 'Save'}
                </button>
                <button
                  onClick={() => setEditing(false)}
                  disabled={saving}
                  style={{
                    padding: '8px 16px', borderRadius: 5, fontSize: 11, fontWeight: 600,
                    background: 'var(--surface)', border: '1px solid var(--border)',
                    color: 'var(--text-muted)', cursor: saving ? 'not-allowed' : 'pointer',
                  }}
                >
                  Cancel
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
    );
  };

  return (
    <div>
      <Link
        to={league.isMember ? '/leagues' : '/leagues?tab=join'}
        style={{
          display: 'inline-block', marginBottom: 12,
          fontSize: 11, fontWeight: 600, color: 'var(--text-muted)', textDecoration: 'none',
        }}
      >
        ← Leagues
      </Link>
      {/* Header bar */}
      <div style={{
        background: 'var(--surface)',
        border: '1px solid var(--border)',
        borderRadius: 6,
        padding: '16px 20px',
        marginBottom: 0,
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'space-between',
        gap: 16,
      }}>
        <div>
          <h2 style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-heading)', marginBottom: 4 }}>{league.name}</h2>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: 10, color: 'var(--text-muted)' }}>
            <span>
              Members: {league.memberCount ?? league.members?.length ?? 0} / {league.maxPlayers}
            </span>
            {(league.memberCount ?? 0) >= league.maxPlayers && (
              <span style={{
                fontSize: 8, padding: '1px 7px', borderRadius: 4, fontWeight: 700,
                background: '#3c2a2a', color: '#e06c75', border: '1px solid #e06c75',
                textTransform: 'uppercase', letterSpacing: '0.5px',
              }}>
                Full
              </span>
            )}
          </div>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
          {statusBadge()}
          {!league.isMember &&
            (league.memberCount ?? 0) < league.maxPlayers &&
            (effectiveStatus === 'NotStarted' || effectiveStatus === 'Scheduled') && (
              <button
                onClick={handleJoin}
                style={{
                  fontSize: 11, fontWeight: 700, padding: '6px 14px', borderRadius: 5,
                  background: 'var(--accent)', color: 'var(--bg)', border: 'none', cursor: 'pointer',
                }}
              >
                Join League
              </button>
            )}
          {league.isMember && (effectiveStatus === 'InProgress' || effectiveStatus === 'Open') && (
            <button
              onClick={() => navigate(`/leagues/${id}/draft`)}
              style={{
                fontSize: 11, fontWeight: 800, padding: '6px 14px', borderRadius: 5,
                background: 'var(--green)', color: '#fff', border: 'none',
                letterSpacing: '0.5px', cursor: 'pointer',
              }}
            >
              Join Draft Room →
            </button>
          )}
          {league.isCommissioner && league.draftStatus === 'NotStarted' && !draftState && (
            <button
              onClick={openDraft}
              style={{
                fontSize: 11, fontWeight: 600, padding: '6px 14px', borderRadius: 5,
                background: 'var(--accent)', border: '1px solid var(--border)', color: '#fff)',
                cursor: 'pointer',
              }}
            >
              Open Draft
            </button>
          )}
        </div>
      </div>

      {/* Tab bar */}
      <div style={{
        display: 'flex',
        background: 'var(--surface)',
        borderLeft: '1px solid var(--border)',
        borderRight: '1px solid var(--border)',
        borderBottom: '1px solid var(--border)',
      }}>
        {tabs.map(t => (
          <button
            key={t.key}
            onClick={() => setActiveTab(t.key)}
            style={{
              flex: 1, padding: '10px 0', fontSize: 11, fontWeight: 600,
              cursor: 'pointer', background: 'transparent', border: 'none',
              color: activeTab === t.key ? 'var(--accent)' : 'var(--text-muted)',
              borderBottom: activeTab === t.key ? '2px solid var(--accent)' : '2px solid transparent',
            }}
          >
            {t.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div style={{
        background: 'var(--surface-2)',
        border: '1px solid var(--border)',
        borderTop: 'none',
        borderBottomLeftRadius: 6,
        borderBottomRightRadius: 6,
        padding: 20,
        minHeight: 200,
      }}>
        {activeTab === 'home' && renderHomeTab()}
        {activeTab === 'scores' && (
          effectiveStatus === 'NotStarted' || effectiveStatus === 'Scheduled'
            ? <div style={{ color: 'var(--text-muted)', fontSize: 11, padding: '20px 0' }}>Scores will appear once the draft begins.</div>
            : <LeagueScoresTab breakdown={breakdown} captions={league.draftableCaptions!} currentUserId={user?.id} />
        )}
        {activeTab === 'members' && renderMembersTab()}
        {activeTab === 'picks' && (
          effectiveStatus === 'NotStarted' || effectiveStatus === 'Scheduled'
            ? <div style={{ color: 'var(--text-muted)', fontSize: 11, padding: '20px 0' }}>Picks will appear once the draft begins.</div>
            : renderPicksTab()
        )}
        {activeTab === 'info' && renderInfoTab()}
      </div>
    </div>
  );
}
