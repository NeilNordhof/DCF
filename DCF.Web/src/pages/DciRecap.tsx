import { useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import type { Caption, DciRecapCorpsEntry, DciRecapResponse, DciRecapScoreRow } from '../types/api';
import { computeRanks } from './DciRecap.helpers';

type Tier = 'plain' | 'tot' | 'section' | 'total';

interface LeafColumn {
  key: string;
  label: string;
  tier: Tier;
  getValue: (entry: DciRecapCorpsEntry) => number;
}

interface ColumnGroup {
  groupLabel: string | null;
  columns: LeafColumn[];
}

function rowsFor(entry: DciRecapCorpsEntry, caption: Caption): DciRecapScoreRow[] {
  return entry.scores.filter(s => s.caption === caption);
}

function subCaptionGroup(caption: Caption, label: string): ColumnGroup {
  return {
    groupLabel: label,
    columns: [
      { key: `${caption}-a`, label: 'Cont', tier: 'plain', getValue: c => rowsFor(c, caption)[0]?.repertoireScore ?? 0 },
      { key: `${caption}-b`, label: 'Achv', tier: 'plain', getValue: c => rowsFor(c, caption)[0]?.performanceScore ?? 0 },
      { key: `${caption}-tot`, label: 'Tot', tier: 'tot', getValue: c => rowsFor(c, caption)[0]?.totalScore ?? 0 },
    ],
  };
}

function multiJudgeGroups(caption: Caption, baseLabel: string, componentLabels: [string, string], corps: DciRecapCorpsEntry[]): ColumnGroup[] {
  const maxJudges = Math.max(1, ...corps.map(c => rowsFor(c, caption).length));

  return Array.from({ length: maxJudges }, (_, judgeIndex) => {
    const judgeName = corps.map(c => rowsFor(c, caption)[judgeIndex]?.judge).find(j => j);
    const groupLabel = maxJudges > 1 ? `${baseLabel}${judgeName ? ` — ${judgeName}` : ` (${judgeIndex + 1})`}` : baseLabel;

    return {
      groupLabel,
      columns: [
        { key: `${caption}-${judgeIndex}-a`, label: componentLabels[0], tier: 'plain', getValue: (c: DciRecapCorpsEntry) => rowsFor(c, caption)[judgeIndex]?.repertoireScore ?? 0 },
        { key: `${caption}-${judgeIndex}-b`, label: componentLabels[1], tier: 'plain', getValue: (c: DciRecapCorpsEntry) => rowsFor(c, caption)[judgeIndex]?.performanceScore ?? 0 },
        { key: `${caption}-${judgeIndex}-tot`, label: 'Tot', tier: 'tot', getValue: (c: DciRecapCorpsEntry) => rowsFor(c, caption)[judgeIndex]?.totalScore ?? 0 },
      ],
    };
  });
}

function standaloneColumn(key: string, label: string, tier: Tier, caption: Caption): ColumnGroup {
  return {
    groupLabel: null,
    columns: [{ key, label, tier, getValue: c => rowsFor(c, caption)[0]?.totalScore ?? 0 }],
  };
}

function buildColumnGroups(corps: DciRecapCorpsEntry[]): ColumnGroup[] {
  return [
    ...multiJudgeGroups('GeneralEffectVisual', 'General Effect 1', ['Rep', 'Perf'], corps),
    ...multiJudgeGroups('GeneralEffectMusic', 'General Effect 2', ['Rep', 'Perf'], corps),
    standaloneColumn('ge-total', 'GE\nTotal', 'section', 'GeneralEffect'),
    subCaptionGroup('VisualProficiency', 'Visual Proficiency'),
    subCaptionGroup('VisualAnalysis', 'Visual Analysis'),
    subCaptionGroup('ColorGuard', 'Color Guard'),
    standaloneColumn('visual-total', 'Visual\nTotal', 'section', 'Visual'),
    subCaptionGroup('Brass', 'Brass'),
    ...multiJudgeGroups('MusicAnalysis', 'Music Analysis', ['Cont', 'Achv'], corps),
    subCaptionGroup('Percussion', 'Percussion'),
    standaloneColumn('music-total', 'Music\nTotal', 'section', 'Music'),
    standaloneColumn('sub-total', 'Sub\nTotal', 'section', 'SubTotal'),
    standaloneColumn('penalties', 'Penalties', 'plain', 'Penalty'),
    standaloneColumn('total-score', 'Total\nScore', 'total', 'Total'),
  ];
}

const tierColor: Record<Tier, string> = {
  plain: 'var(--text-muted)',
  tot: 'var(--text-heading)',
  section: 'var(--accent)',
  total: 'var(--accent)',
};

export function DciRecap() {
  const { showId } = useParams<{ showId: string }>();
  const [data, setData] = useState<DciRecapResponse | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [sortKey, setSortKey] = useState('total-score');
  const [sortDesc, setSortDesc] = useState(true);
  const [highlighted, setHighlighted] = useState<string | null>(null);
  const groupRowRef = useRef<HTMLTableRowElement>(null);
  const [leafTop, setLeafTop] = useState(40);

  useEffect(() => {
    if (showId) {
      api.getDciRecap(showId).then(setData).catch(() => setNotFound(true));
    }
  }, [showId]);

  useEffect(() => {
    if (groupRowRef.current) {
      setLeafTop(groupRowRef.current.getBoundingClientRect().height);
    }
  }, [data]);

  if (notFound) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>Show not found.</p>;
  }

  if (!data) {
    return <p style={{ color: 'var(--text-muted)', fontSize: 11 }}>Loading recap...</p>;
  }

  const groups = buildColumnGroups(data.corps);
  const leafColumns = groups.flatMap(g => g.columns);
  const colByKey = new Map(leafColumns.map(c => [c.key, c]));
  const activeCol = colByKey.get(sortKey)!;
  const ranksByColumn = new Map(leafColumns.map(col => [col.key, computeRanks(data.corps, col.getValue, col.key === 'penalties')]));
  const overallRanks = ranksByColumn.get('total-score')!;

  const sortedCorps = [...data.corps].sort((a, b) => {
    const cmp = activeCol.getValue(b) - activeCol.getValue(a);
    return sortDesc ? cmp : -cmp;
  });

  function toggleSort(key: string) {
    if (sortKey === key) {
      setSortDesc(d => !d);
    } else {
      setSortKey(key);
      setSortDesc(true);
    }
  }

  function arrowFor(key: string) {
    return sortKey === key ? (sortDesc ? '▼' : '▲') : '';
  }

  const cellBase: React.CSSProperties = { padding: '6px 9px', borderBottom: '1px solid var(--border)', borderRight: '1px solid var(--border)', textAlign: 'center' };
  const groupThBase: React.CSSProperties = { ...cellBase, position: 'sticky', top: 0, zIndex: 3, background: 'var(--surface-2)', color: 'var(--text-heading)', fontSize: 10, textTransform: 'uppercase', letterSpacing: '0.4px', fontWeight: 700, whiteSpace: 'pre-line', lineHeight: 1.3 };
  const leafThBase: React.CSSProperties = { ...cellBase, position: 'sticky', top: leafTop, zIndex: 3, background: 'var(--surface)', color: 'var(--text-muted)', fontSize: 9, textTransform: 'uppercase', letterSpacing: '0.3px', fontWeight: 600, cursor: 'pointer', userSelect: 'none', whiteSpace: 'nowrap' };

  return (
    <div>
      <p style={{ marginBottom: 14 }}><Link to="/dci?tab=scores" style={{ fontSize: 11, color: 'var(--text-muted)', textDecoration: 'none' }}>← Back to Scores</Link></p>
      <div style={{ marginBottom: 16 }}>
        <div style={{ fontSize: 18, fontWeight: 700, color: 'var(--text-heading)' }}>{data.show.name}</div>
        <div style={{ fontSize: 11, color: 'var(--text-muted)', marginTop: 4 }}>
          {new Date(`${data.show.date}T00:00:00Z`).toLocaleDateString('en-US', { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric', timeZone: 'UTC' })}
          {data.show.location ? ` · ${data.show.location}` : ''}
        </div>
      </div>
      <div style={{ maxHeight: 650, overflow: 'auto', border: '1px solid var(--border)', borderRadius: 5 }}>
        <table style={{ borderCollapse: 'separate', borderSpacing: 0, fontSize: 11, whiteSpace: 'nowrap', width: '100%' }}>
          <thead>
            <tr ref={groupRowRef}>
              <th rowSpan={2} style={{ ...groupThBase, left: 0, zIndex: 5, textAlign: 'left', minWidth: 156, whiteSpace: 'nowrap' }}>Corps</th>
              {groups.map(group => {
                if (group.groupLabel === null) {
                  const col = group.columns[0];

                  return (
                    <th
                      key={col.key}
                      rowSpan={2}
                      onClick={() => toggleSort(col.key)}
                      style={{ ...groupThBase, color: tierColor[col.tier], width: col.key === 'penalties' ? undefined : 64, cursor: 'pointer' }}
                    >
                      {col.label}
                      <span style={{ position: 'absolute', right: 3, top: '50%', transform: 'translateY(-50%)', color: 'var(--accent)', fontSize: 8 }}>{arrowFor(col.key)}</span>
                    </th>
                  );
                }

                return (
                  <th key={`${group.groupLabel}-${group.columns[0].key}`} colSpan={group.columns.length} style={groupThBase}>
                    {group.groupLabel}
                  </th>
                );
              })}
            </tr>
            <tr>
              {groups.flatMap(group => (group.groupLabel === null ? [] : group.columns)).map(col => (
                <th
                  key={col.key}
                  onClick={() => toggleSort(col.key)}
                  style={{ ...leafThBase, color: col.tier === 'tot' ? tierColor.tot : leafThBase.color }}
                >
                  {col.label}
                  <span style={{ position: 'absolute', right: 3, top: '50%', transform: 'translateY(-50%)', color: 'var(--accent)', fontSize: 8 }}>{arrowFor(col.key)}</span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {sortedCorps.map(corps => {
              const isHighlighted = highlighted === corps.corpsId;
              const rowBg = isHighlighted ? 'var(--accent-bg)' : undefined;

              return (
                <tr key={corps.corpsId}>
                  <td
                    onClick={() => setHighlighted(h => (h === corps.corpsId ? null : corps.corpsId))}
                    style={{ ...cellBase, position: 'sticky', left: 0, zIndex: 1, background: rowBg ?? 'var(--surface)', textAlign: 'left', minWidth: 156, cursor: 'pointer' }}
                  >
                    <span style={{ color: 'var(--text-muted)', fontWeight: 700, fontSize: 10, marginRight: 6 }}>{overallRanks.get(corps)}</span>
                    <span style={{ color: 'var(--text-heading)', fontWeight: 600 }}>{corps.corpsName}</span>
                  </td>
                  {leafColumns.map(col => (
                    <td key={col.key} style={{ ...cellBase, background: rowBg }}>
                      <div style={{ color: isHighlighted ? 'var(--text-heading)' : tierColor[col.tier], fontWeight: col.tier === 'plain' ? 400 : 700 }}>{col.getValue(corps).toFixed(3)}</div>
                      <div style={{ color: 'var(--text-faint)', fontSize: 9, marginTop: 1 }}>{ranksByColumn.get(col.key)!.get(corps)}</div>
                    </td>
                  ))}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
