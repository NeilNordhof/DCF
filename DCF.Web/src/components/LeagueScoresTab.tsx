import type { MemberScoreBreakdown } from '../types/api';

interface Props {
  breakdown: MemberScoreBreakdown[];
  captions: string[];
  currentUserId?: string;
}

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
            <th style={{
              position: 'sticky', left: 0, zIndex: 2,
              minWidth: 80, background: 'var(--surface)',
              borderRight: '1px solid var(--border-subtle)',
              borderBottom: '1px solid var(--border)',
              padding: '6px 10px',
            }} />
            {captions.map(cap => (
              <th
                key={cap}
                colSpan={3}
                style={{
                  fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px',
                  color: 'var(--text-faint)', fontWeight: 700, textAlign: 'center',
                  padding: '6px 4px',
                  borderBottom: '1px solid var(--border)',
                  borderRight: '1px solid var(--border-subtle)',
                }}
              >
                {cap}
              </th>
            ))}
            <th style={{
              fontSize: 8, textTransform: 'uppercase', letterSpacing: '0.5px',
              color: 'var(--text-faint)', fontWeight: 700, textAlign: 'center',
              padding: '6px 8px',
              borderBottom: '1px solid var(--border)',
            }}>
              Total
            </th>
          </tr>
          <tr>
            <th style={{
              position: 'sticky', left: 0, zIndex: 2,
              background: 'var(--surface)',
              borderRight: '1px solid var(--border-subtle)',
              borderBottom: '1px solid var(--border)',
              padding: '4px 10px',
              fontSize: 8, color: 'var(--text-faint)', fontWeight: 600, textAlign: 'left',
            }}>
              Player
            </th>
            {captions.flatMap(cap =>
              ['Corps', 'Score', 'Avg'].map(sub => (
                <th
                  key={`${cap}-${sub}`}
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
            <th style={{
              fontSize: 8, color: 'var(--text-faint)', fontWeight: 600,
              padding: '4px 8px',
              borderBottom: '1px solid var(--border)',
              textAlign: 'right',
            }} />
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
                  {isFirstRow && (
                    <td
                      rowSpan={maxRows}
                      style={{
                        position: 'sticky', left: 0, zIndex: 1,
                        background: isMe ? '#130d1f' : 'var(--surface)',
                        borderRight: '1px solid var(--border-subtle)',
                        padding: '6px 10px',
                        minWidth: 80,
                        verticalAlign: 'middle',
                        fontSize: 10, fontWeight: 600,
                        color: 'var(--text-heading)',
                        borderBottom: '1px solid var(--border)',
                      }}
                    >
                      {player.displayName}
                    </td>
                  )}
                  {captions.flatMap(cap => {
                    const cb = player.captions[cap];
                    const pick = cb?.picks[rowIdx];
                    const avg = cb?.avg ?? 0;

                    return [
                      <td key={`${cap}-corps`} style={{
                        padding: '4px 8px', color: 'var(--text)',
                        borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                      }}>
                        {pick?.corpsName ?? ''}
                      </td>,
                      <td key={`${cap}-score`} style={{
                        padding: '4px 8px', color: 'var(--text-muted)',
                        borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                      }}>
                        {pick?.score != null ? pick.score.toFixed(3) : pick ? '—' : ''}
                      </td>,
                      <td key={`${cap}-avg`} style={{
                        padding: '4px 8px', textAlign: 'right', fontWeight: 600,
                        color: isMe ? 'var(--accent)' : 'var(--text-muted)',
                        borderRight: '1px solid var(--border-subtle)',
                        borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                      }}>
                        {isFirstRow && avg > 0 ? avg.toFixed(3) : ''}
                      </td>,
                    ];
                  })}
                  <td style={{
                    padding: '4px 8px', textAlign: 'right',
                    fontSize: 12, fontWeight: 900,
                    color: isMe ? 'var(--accent)' : 'var(--text-heading)',
                    borderBottom: isLastRow ? '1px solid var(--border)' : undefined,
                  }}>
                    {isFirstRow && player.totalScore > 0 ? player.totalScore.toFixed(3) : ''}
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
