import type { ComputedCaption, MemberScoreBreakdown } from '../types/api';
import { CorpsIcon } from './CorpsIcon';

interface Props {
  breakdown: MemberScoreBreakdown[];
  captions: ComputedCaption[];
  currentUserId?: string;
}

const CAPTION_LABELS: Record<ComputedCaption, string> = {
  GeneralEffectCombined: 'GENERAL EFFECT',
  GeneralEffect1: 'GENERAL EFFECT 1',
  GeneralEffect2: 'GENERAL EFFECT 2',
  VisualCombined: 'VISUAL',
  Visual: 'VISUAL',
  Colorguard: 'COLORGUARD',
  VisualProficiency: 'VISUAL PROFICIENCY',
  VisualAnalysis: 'VISUAL ANALYSIS',
  MusicCombined: 'MUSIC',
  Brass: 'BRASS',
  Percussion: 'PERCUSSION',
  MusicAnalysis: 'MUSIC ANALYSIS',
};

export function LeagueScoresTab({ breakdown, captions, currentUserId }: Props) {
  if (breakdown.length === 0) {
    return (
      <div style={{ padding: 24, color: 'var(--text-muted)', fontSize: 11 }}>
        No scores available yet.
      </div>
    );
  }

  return (
    <div style={{ overflowX: 'auto' }}>
      <table style={{ borderCollapse: 'collapse', fontSize: 10, whiteSpace: 'nowrap' }}>
        <thead>
          <tr>
            <th
              scope="col"
              aria-label="Player"
              style={{
                position: 'sticky', left: 0, zIndex: 2,
                minWidth: 80, background: 'var(--surface)',
                borderRight: '1px solid var(--border-subtle)',
                borderBottom: '1px solid var(--border)',
                padding: '6px 10px',
              }}
            />
            {captions.map(cap => (
              <th
                key={cap}
                scope="colgroup"
                colSpan={3}
                style={{
                  fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px',
                  color: 'var(--text-faint)', fontWeight: 700, textAlign: 'center',
                  padding: '6px 4px',
                  borderBottom: '1px solid var(--border)',
                  borderRight: '1px solid var(--border-subtle)',
                }}
              >
                {CAPTION_LABELS[cap]}
              </th>
            ))}
            <th
              scope="col"
              style={{
                position: 'sticky', right: 0, zIndex: 2,
                fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px',
                color: 'var(--text-faint)', fontWeight: 700, textAlign: 'center',
                padding: '6px 8px',
                background: 'var(--surface)',
                borderBottom: '1px solid var(--border)',
                borderLeft: '1px solid var(--border-subtle)',
              }}
            >
              Total
            </th>
          </tr>
          <tr>
            <th
              scope="col"
              style={{
                position: 'sticky', left: 0, zIndex: 2,
                background: 'var(--surface)',
                borderRight: '1px solid var(--border-subtle)',
                borderBottom: '1px solid var(--border)',
                padding: '4px 10px',
                fontSize: 8, color: 'var(--text-faint)', fontWeight: 600, textAlign: 'left',
              }}
            >
              Player
            </th>
            {captions.flatMap(cap =>
              ['Corps', 'Score', 'Avg'].map(sub => (
                <th
                  key={`${cap}-${sub}`}
                  scope="col"
                  style={{
                    fontSize: 8, color: 'var(--text-faint)', fontWeight: 600,
                    padding: '4px 8px',
                    borderBottom: '1px solid var(--border)',
                    borderRight: sub === 'Avg' ? '1px solid var(--border-subtle)' : undefined,
                    textAlign: sub === 'Avg' ? 'right' : 'left',
                  }}
                >
                  {sub}
                </th>
              ))
            )}
            <th
              scope="col"
              aria-label="Total score"
              style={{
                position: 'sticky', right: 0, zIndex: 2,
                fontSize: 8, color: 'var(--text-faint)', fontWeight: 600,
                padding: '4px 8px',
                background: 'var(--surface)',
                borderBottom: '1px solid var(--border)',
                borderLeft: '1px solid var(--border-subtle)',
                textAlign: 'right',
              }}
            />
          </tr>
        </thead>
        <tbody>
          {breakdown.map(player => {
            const isMe = player.userId === currentUserId;
            const maxRows = Math.max(1, ...captions.map(cap => player.captions[cap]?.picks.length ?? 0));

            return Array.from({ length: maxRows }, (_, rowIdx) => {
              const isFirstRow = rowIdx === 0;
              const isLastRow = rowIdx === maxRows - 1;

              return (
                <tr key={`${player.userId}-${rowIdx}`}>
                  <td
                    style={{
                      position: 'sticky', left: 0, zIndex: 1,
                      background: isMe ? 'var(--surface-me)' : 'var(--surface)',
                      borderRight: '1px solid var(--border-subtle)',
                      padding: '6px 10px',
                      minWidth: 80,
                      verticalAlign: 'middle',
                      fontSize: 10, fontWeight: 600,
                      color: 'var(--text-heading)',
                      borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                    }}
                  >
                    {isFirstRow ? player.displayName : ''}
                  </td>
                  {captions.flatMap(cap => {
                    const cb = player.captions[cap];
                    const pick = cb?.picks[rowIdx];
                    const avg = cb?.avg ?? 0;
                    const capPickCount = cb?.picks.length ?? 0;
                    const rowSpan = capPickCount || 1;

                    let avgCell;

                    if (isFirstRow) {
                      avgCell = (
                        <td
                          key={`${cap}-avg`}
                          rowSpan={rowSpan}
                          style={{
                            padding: '4px 8px', textAlign: 'right', fontWeight: 700, fontSize: 12,
                            color: isMe ? 'var(--accent)' : 'var(--text-heading)',
                            borderRight: '1px solid var(--border-subtle)',
                            verticalAlign: 'middle',
                            borderBottom: rowSpan >= maxRows ? '1px solid var(--border)' : undefined,
                          }}
                        >
                          {avg > 0 ? avg.toFixed(3) : ''}
                        </td>
                      );
                    } else if (rowIdx < capPickCount) {
                      avgCell = null;
                    } else {
                      avgCell = (
                        <td
                          key={`${cap}-avg`}
                          style={{
                            borderRight: '1px solid var(--border-subtle)',
                            borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                          }}
                        />
                      );
                    }

                    return [
                      <td key={`${cap}-corps`} style={{
                        padding: '4px 8px',
                        borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                      }}>
                        {pick
                          ? <CorpsIcon name={pick.corpsName} iconUrl={pick.iconUrl} size={22} />
                          : ''}
                      </td>,
                      <td key={`${cap}-score`} style={{
                        padding: '4px 8px', color: 'var(--text-muted)',
                        borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                      }}>
                        {pick?.score != null ? pick.score.toFixed(3) : pick ? '—' : ''}
                      </td>,
                      avgCell,
                    ];
                  })}
                  <td style={{
                    position: 'sticky', right: 0, zIndex: 1,
                    padding: '4px 8px', textAlign: 'right',
                    fontSize: 12, fontWeight: 900,
                    color: isMe ? 'var(--accent)' : 'var(--text-heading)',
                    background: isMe ? 'var(--surface-me)' : 'var(--surface)',
                    borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                    borderLeft: '1px solid var(--border-subtle)',
                  }}>
                    {isFirstRow ? player.totalScore.toFixed(3) : ''}
                  </td>
                </tr>
              );
            });
          })}
        </tbody>
      </table>
    </div>
  );
}
