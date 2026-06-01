import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useMqtt } from '../mqtt/useMqtt';
import { useDraftPresence } from '../mqtt/useDraftPresence';
import { useUser } from '../context/UserContext';
import { CorpsIcon } from '../components/CorpsIcon';
import { Nav } from '../components/Nav';
import type { SeasonCorps, DraftState, League, PickPreview } from '../types/api';

export function DraftRoom() {
  const { id } = useParams<{ id: string }>();
  const { user } = useUser();
  const navigate = useNavigate();

  const [league, setLeague] = useState<League | null>(null);
  const [corps, setCorps] = useState<SeasonCorps[]>([]);
  const [selectedCell, setSelectedCell] = useState<{ corpsId: string; caption: string } | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [activeTab, setActiveTab] = useState<'order' | 'picks'>('order');
  const [activePicksPlayer, setActivePicksPlayer] = useState<string | null>(null);
  const [now, setNow] = useState(() => Date.now());
  const [error, setError] = useState<string | null>(null);

  const draftState = useMqtt<DraftState>(`dcf/leagues/${id}/draft`);
  const pickPreview = useMqtt<PickPreview>(`dcf/leagues/${id}/draft/pick`);
  const { publishPickPreview } = useDraftPresence(id!, user?.id);

  useEffect(() => {
    if (!id) return;
    api.getLeague(id)
      .then(l => {
        setLeague(l);
        if (l.seasonId) {
          api.getSeasonCorps(l.seasonId).then(setCorps).catch(() => {});
        }
      })
      .catch(() => setError('Failed to load league.'));
  }, [id]);

  // Redirect guard — only allow Open, InProgress, Completed
  useEffect(() => {
    if (!league) return;

    if (league.draftStatus === 'NotStarted') {
      navigate(`/leagues/${id}`);
    }
  }, [league, id, navigate]);

  // Countdown timer — only ticks during Open lobby
  useEffect(() => {
    if (draftState?.status !== 'Open' && league?.draftStatus !== 'Scheduled') return;
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [draftState?.status, league?.draftStatus]);

  if (error) return <div style={{ padding: 16, color: 'var(--text)' }}>{error}</div>;
  if (!league || !draftState) return <div style={{ padding: 16, color: 'var(--text-muted)' }}>Loading…</div>;

  const status = draftState.status;
  const isMyTurn = status === 'InProgress' && draftState.currentDrafterId === user?.id;
  const takenSet = new Set(draftState.picks.map(p => `${p.corpsId}|${p.caption}`));
  const isTaken = (corpsId: string, caption: string) => takenSet.has(`${corpsId}|${caption}`);
  const isOnline = (userId: string) => (draftState.onlineUserIds ?? []).includes(userId);

  const currentDrafter = draftState.members.find(m => m.userId === draftState.currentDrafterId);

  // Pick preview is valid only when the cell is still available and it's the current drafter's preview
  const validPreview = (
    pickPreview &&
    pickPreview.userId === draftState.currentDrafterId &&
    !isMyTurn &&
    !isTaken(pickPreview.corpsId, pickPreview.caption)
  ) ? pickPreview : null;

  const handleCellClick = (corpsId: string, caption: string) => {
    if (!isMyTurn || isTaken(corpsId, caption)) return;
    setSelectedCell({ corpsId, caption });
    publishPickPreview(corpsId, caption);
  };

  const submitPick = async () => {
    if (!id || !selectedCell || submitting) return;
    setSubmitting(true);
    try {
      await api.submitPick(id, selectedCell.corpsId, selectedCell.caption);
      setSelectedCell(null);
    }
    finally {
      setSubmitting(false);
    }
  };

  const skipPick = () =>
  {
    if (id) api.skipPick(id).catch(() => {});
  };

  const startDraft = () =>
  {
    if (id) api.startDraft(id).catch(() => {});
  };

  const getCountdown = () => {
    if (!league.draftStartTime) return '--:--:--';
    const diff = new Date(league.draftStartTime).getTime() - now;
    if (diff <= 0) return '00:00:00';
    const h = Math.floor(diff / 3600000);
    const m = Math.floor((diff % 3600000) / 60000);
    const s = Math.floor((diff % 60000) / 1000);
    return [h, m, s].map(n => String(n).padStart(2, '0')).join(':');
  };

  // ── Top bar ──────────────────────────────────────────────────────────────

  const renderTopBar = () => {
    if (status === 'Open' || (status !== 'InProgress' && status !== 'Completed' && league.draftStatus === 'Scheduled')) {
      return (
        <div style={{ background: 'linear-gradient(90deg, #0f1a0f, #101810)', borderBottom: '2px solid var(--green-border)', padding: '10px 16px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
          <div>
            <div style={{ fontSize: 9, letterSpacing: '0.5px', textTransform: 'uppercase', color: 'var(--green)', fontWeight: 700 }}>Draft Begins In</div>
            <div style={{ fontSize: 26, fontWeight: 900, color: 'var(--text-h)', fontVariantNumeric: 'tabular-nums' }}>{getCountdown()}</div>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
            <div style={{ textAlign: 'right' }}>
              {league.draftStartTime && (
                <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{new Date(league.draftStartTime).toLocaleString()}</div>
              )}
              <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{league.name}</div>
            </div>
            {league.isCommissioner && (
              <button onClick={startDraft} style={{ border: '1px solid var(--green-border)', color: 'var(--green)', background: 'transparent', borderRadius: 5, padding: '4px 10px', fontSize: 10, cursor: 'pointer', fontWeight: 600 }}>
                Start Early
              </button>
            )}
          </div>
        </div>
      );
    }

    if (status === 'InProgress') {
      return (
        <div style={{ background: 'linear-gradient(90deg, #2e1065, #1a1535)', borderBottom: '2px solid var(--accent)', padding: '10px 16px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
          <div>
            <div style={{ fontSize: 9, letterSpacing: '0.5px', textTransform: 'uppercase', color: 'var(--accent)', fontWeight: 700 }}>
              {isMyTurn ? 'On the Clock' : 'Now Picking'}
            </div>
            <div style={{ fontSize: 15, fontWeight: 800, color: 'var(--text-h)' }}>
              {isMyTurn ? (user?.displayName ?? '—') : (currentDrafter?.displayName ?? '—')}
            </div>
          </div>
          <div style={{ textAlign: 'right' }}>
            <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>
              Round {Math.floor(draftState.currentPickNumber / draftState.members.length) + 1} · Pick {(draftState.currentPickNumber % draftState.members.length) + 1}
            </div>
            <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{league.name}</div>
          </div>
        </div>
      );
    }

    return (
      <div style={{ background: 'var(--surface)', borderBottom: '1px solid var(--border)', padding: '10px 16px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
        <div style={{ fontSize: 12, fontWeight: 700, color: 'var(--text-muted)' }}>Draft Complete</div>
        <div style={{ fontSize: 10, color: 'var(--text-muted)' }}>{league.name}</div>
      </div>
    );
  };

  // ── Pick grid ─────────────────────────────────────────────────────────────

  const renderGrid = () => {
    const captions = league.draftableCaptions!;
    const gridLocked = status !== 'InProgress' || !isMyTurn;
    const cellWidth = captions.length <= 3 ? Math.min(88, Math.floor(176 / captions.length)) : 44;

    return (
      <div style={{ flex: 1, overflowY: 'auto', padding: 12 }}>
        {status === 'Open' && (
          <div style={{ fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 8 }}>
            Pick board locks until the draft begins
          </div>
        )}
        <table style={{ borderCollapse: 'separate', borderSpacing: 2 }}>
          <thead>
            <tr>
              {captions.map(cap => (
                <th key={cap} style={{ width: cellWidth, fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-muted)', paddingBottom: 6, textAlign: 'center', fontWeight: 600 }}>
                  {cap}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {corps.map(c => (
              <tr key={c.id}>
                {captions.map(cap => {
                  const taken = isTaken(c.id, cap);
                  const selected = !gridLocked && selectedCell?.corpsId === c.id && selectedCell?.caption === cap;
                  const previewed = !taken && !selected && validPreview?.corpsId === c.id && validPreview?.caption === cap;
                  const isLobby = status === 'Open';

                  let bg = 'var(--green-bg)';
                  let border = '1px solid var(--green-border)';
                  let boxShadow = 'none';
                  const cursor = gridLocked || taken ? 'not-allowed' : 'pointer';
                  let content: ReactNode;

                  if (taken) {
                    bg = '#12141a';
                    border = '1px solid var(--border-subtle)';
                    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} style={{ opacity: 0.25 }} />;
                  }
                  else if (selected) {
                    bg = 'var(--accent-bg)';
                    border = '2px solid var(--accent)';
                    boxShadow = '0 0 10px var(--accent-bg)';
                    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={34} style={{ outline: '1px solid var(--accent)', outlineOffset: 2 }} />;
                  }
                  else if (previewed) {
                    const drafter = draftState.members.find(m => m.userId === validPreview!.userId);
                    bg = '#1e1430';
                    border = '1px dashed var(--accent-border)';
                    content = (
                      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
                        <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={26} />
                        <span style={{ color: 'var(--text-muted)', fontSize: 7, lineHeight: 1 }}>
                          {drafter?.displayName.split(' ')[0] ?? ''}
                        </span>
                      </div>
                    );
                  }
                  else {
                    content = <CorpsIcon name={c.name} iconUrl={c.iconUrl} size={36} />;
                  }

                  return (
                    <td key={cap}>
                      <div
                        onClick={() => handleCellClick(c.id, cap)}
                        style={{
                          width: cellWidth,
                          height: 44,
                          background: bg,
                          border,
                          borderRadius: 4,
                          boxShadow,
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                          cursor,
                          opacity: isLobby ? 0.45 : 1,
                          userSelect: 'none',
                          transition: 'background 0.1s',
                          pointerEvents: gridLocked ? 'none' : 'auto',
                        }}
                      >
                        {content}
                      </div>
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  };

  // ── Submit bar ────────────────────────────────────────────────────────────

  const renderSubmitBar = () => {
    if (status === 'Completed') return null;

    const selectedCorps = corps.find(c => c.id === selectedCell?.corpsId);
    const selectionLabel = isMyTurn && selectedCell
      ? `${selectedCorps?.name ?? '—'} · ${selectedCell.caption}`
      : '— · —';
    const canSubmit = isMyTurn && !!selectedCell && !submitting;

    return (
      <div style={{ background: 'var(--surface)', border: '1px solid var(--accent-border)', borderRadius: 6, padding: '8px 12px', margin: '0 12px 12px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8, flexShrink: 0 }}>
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)' }}>Selected</div>
          <div style={{ fontSize: 11, color: canSubmit ? 'var(--text-h)' : 'var(--text-muted)' }}>{selectionLabel}</div>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          {league.isCommissioner && status === 'InProgress' && !isMyTurn && (
            <button
              onClick={skipPick}
              style={{ background: 'var(--surface)', border: '1px solid var(--border)', color: 'var(--text)', borderRadius: 5, padding: '5px 10px', fontSize: 10, cursor: 'pointer', fontWeight: 600 }}
            >
              Skip Pick
            </button>
          )}
          <button
            onClick={submitPick}
            disabled={!canSubmit}
            style={{
              background: canSubmit ? 'var(--accent)' : 'var(--border)',
              color: canSubmit ? '#0d0f14' : 'var(--text-faint)',
              border: 'none', borderRadius: 5, padding: '5px 14px',
              fontSize: 10, fontWeight: 800, letterSpacing: '0.5px',
              textTransform: 'uppercase', cursor: canSubmit ? 'pointer' : 'not-allowed',
            }}
          >
            Submit Pick
          </button>
        </div>
      </div>
    );
  };

  // ── Side panel — Draft Order tab ──────────────────────────────────────────

  const renderDraftOrderTab = () => {
    if (status === 'Open') {
      const onlineCount = draftState.draftOrder.filter(m => isOnline(m.userId)).length;

      return (
        <div>
          <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 8 }}>Draft Order</div>
          {draftState.draftOrder.map((m, i) => (
            <div key={m.userId} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '5px 0', borderBottom: '1px solid var(--border-subtle)' }}>
              <div style={{ width: 20, height: 20, borderRadius: '50%', background: 'var(--surface)', border: '1px solid var(--border)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 9, color: 'var(--text-muted)', flexShrink: 0 }}>
                {i + 1}
              </div>
              <span style={{ fontSize: 7, color: isOnline(m.userId) ? 'var(--green)' : 'var(--text-faint)', flexShrink: 0 }}>
                {isOnline(m.userId) ? '●' : '○'}
              </span>
              <span style={{ fontSize: 11, color: 'var(--text-h)' }}>{m.displayName}</span>
            </div>
          ))}
          <div style={{ fontSize: 9, color: 'var(--text-muted)', marginTop: 8 }}>
            {onlineCount} of {draftState.draftOrder.length} members online
          </div>
        </div>
      );
    }

    const n = draftState.draftOrder.length;
    const totalPicks = n * league.draftableCaptions!.length * league.corpsPerCaption!;

    const upcomingOrder: Array<{ userId: string; displayName: string }> = [];

    if (status === 'InProgress') {
      for (let pick = draftState.currentPickNumber + 1; pick < Math.min(draftState.currentPickNumber + 6, totalPicks); pick++) {
        const round = Math.floor(pick / n);
        const pos = pick % n;
        const idx = round % 2 === 0 ? pos : n - 1 - pos;
        upcomingOrder.push(draftState.draftOrder[idx]);
      }
    }

    return (
      <div>
        {draftState.picks.length > 0 && (
          <div style={{ marginBottom: 8 }}>
            <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>Completed</div>
            {draftState.picks.map(p => (
              <div key={p.pickNumber} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0', opacity: 0.55 }}>
                <div style={{ width: 18, height: 18, borderRadius: '50%', background: 'var(--surface)', border: '1px solid var(--border)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 8, color: 'var(--text-muted)', flexShrink: 0 }}>
                  {p.pickNumber + 1}
                </div>
                <span style={{ fontSize: 10, color: 'var(--text)' }}>{p.displayName} — {p.corpsName} ({p.caption})</span>
              </div>
            ))}
          </div>
        )}
        {status === 'InProgress' && currentDrafter && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '6px 8px', margin: '4px 0', background: 'var(--accent-bg)', border: '1px solid var(--accent-border)', borderRadius: 5 }}>
            <div style={{ width: 18, height: 18, borderRadius: '50%', background: 'var(--accent-bg)', border: '1px solid var(--accent)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 8, color: 'var(--accent)', flexShrink: 0 }}>
              {draftState.currentPickNumber + 1}
            </div>
            <span style={{ fontSize: 11, fontWeight: 700, color: 'var(--text-h)' }}>{currentDrafter.displayName}</span>
          </div>
        )}
        {upcomingOrder.length > 0 && (
          <div style={{ marginTop: 8 }}>
            <div style={{ fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px', color: 'var(--text-faint)', marginBottom: 6 }}>Up Next</div>
            {upcomingOrder.map((m, i) => (
              <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '4px 0', opacity: 0.6 }}>
                <div style={{ width: 18, height: 18, borderRadius: '50%', background: 'var(--surface)', border: '1px solid var(--border)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 8, color: 'var(--text-muted)', flexShrink: 0 }}>
                  {draftState.currentPickNumber + i + 2}
                </div>
                <span style={{ fontSize: 10, color: 'var(--text)' }}>{m.displayName}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    );
  };

  // ── Side panel — Picks tab ────────────────────────────────────────────────

  const renderPicksTab = () => {
    const players = draftState.members;
    const effectivePicksPlayer = activePicksPlayer ?? players[0]?.userId ?? null;
    const currentPlayer = players.find(m => m.userId === effectivePicksPlayer) ?? players[0];
    if (!currentPlayer) return null;

    const captions = league.draftableCaptions!;
    const playerPicks = draftState.picks.filter(p => p.userId === currentPlayer.userId);

    return (
      <div>
        <div style={{ display: 'flex', gap: 4, marginBottom: 12, flexWrap: 'wrap' }}>
          {players.map(m => (
            <button
              key={m.userId}
              onClick={() => setActivePicksPlayer(m.userId)}
              style={{
                padding: '4px 10px', borderRadius: 12, fontSize: 10, fontWeight: 600,
                cursor: 'pointer', border: 'none',
                background: effectivePicksPlayer === m.userId ? 'var(--accent)' : 'var(--surface)',
                color: effectivePicksPlayer === m.userId ? '#0d0f14' : 'var(--text-muted)',
              }}
            >
              {m.displayName.split(' ')[0]}
            </button>
          ))}
        </div>
        {captions.map(cap => {
          const capPicks = playerPicks.filter(p => p.caption === cap);
          const filled = capPicks.length;
          const total = league.corpsPerCaption!;

          return (
            <div key={cap} style={{ marginBottom: 10 }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 4 }}>
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
                    <div key={i} style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '5px 8px', background: 'var(--surface)', border: '1px solid var(--border)', borderRadius: 5, marginBottom: 4 }}>
                      <div style={{ width: 18, height: 18, borderRadius: '50%', background: 'var(--accent-bg)', border: '1px solid var(--accent-border)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 8, color: 'var(--accent)', flexShrink: 0 }}>
                        #{pick.pickNumber + 1}
                      </div>
                      <div>
                        <div style={{ fontSize: 10, fontWeight: 600, color: 'var(--text-h)' }}>{pick.corpsName}</div>
                        <div style={{ fontSize: 8, color: 'var(--text-muted)' }}>Pick #{pick.pickNumber + 1} overall</div>
                      </div>
                    </div>
                  );
                }

                return (
                  <div key={i} style={{ padding: '5px 8px', border: '1px dashed var(--border)', borderRadius: 5, marginBottom: 4 }}>
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

  // ── Layout ────────────────────────────────────────────────────────────────

  return (
    <>
      <Nav />
      <div style={{ height: 'calc(100vh - 44px)', overflow: 'hidden', background: 'var(--bg)', color: 'var(--text)' }}>
        <div style={{ maxWidth: 1200, width: '100%', height: '100%', margin: '0 auto', padding: '0 20px', boxSizing: 'border-box', display: 'flex' }}>
          {/* Left — bar + grid */}
          <div style={{ flex: 1, display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
            {renderTopBar()}
            {renderGrid()}
            {renderSubmitBar()}
          </div>
          {/* Right — side panel */}
          <div style={{ width: 280, background: 'var(--surface-2)', borderLeft: '1px solid var(--border)', display: 'flex', flexDirection: 'column', flexShrink: 0 }}>
            <div style={{ display: 'flex', borderBottom: '1px solid var(--border)', background: 'var(--surface)', flexShrink: 0 }}>
              {(['order', 'picks'] as const).map(tab => (
                <button
                  key={tab}
                  onClick={() => setActiveTab(tab)}
                  style={{
                    flex: 1, padding: '10px 0', fontSize: 11, fontWeight: 600, cursor: 'pointer',
                    background: 'transparent', border: 'none',
                    color: activeTab === tab ? 'var(--accent)' : 'var(--text-muted)',
                    borderBottom: activeTab === tab ? '2px solid var(--accent)' : '2px solid transparent',
                  }}
                >
                  {tab === 'order' ? 'Draft Order' : 'Picks'}
                </button>
              ))}
            </div>
            <div style={{ flex: 1, overflowY: 'auto', padding: 12 }}>
              {activeTab === 'order' ? renderDraftOrderTab() : renderPicksTab()}
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
