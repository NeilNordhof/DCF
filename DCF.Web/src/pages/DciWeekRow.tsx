import { useRef } from 'react';
import type { ReactNode } from 'react';

export function WeekRow<T>({ weekLabel, items, renderCard }: { weekLabel: string; items: T[]; renderCard: (item: T) => ReactNode }) {
  const rowRef = useRef<HTMLDivElement>(null);

  function scroll(dir: 1 | -1) {
    rowRef.current?.scrollBy({ left: dir * 250, behavior: 'smooth' });
  }

  return (
    <div>
      <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-heading)', margin: '22px 0 10px' }}>
        {weekLabel} <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-muted)', marginLeft: 6 }}>{items.length} show{items.length === 1 ? '' : 's'}</span>
      </div>
      <div style={{ position: 'relative' }}>
        <button onClick={() => scroll(-1)} aria-label="Scroll left" style={{ position: 'absolute', left: -12, top: '50%', transform: 'translateY(-50%)', zIndex: 3, width: 26, height: 26, borderRadius: '50%', border: '1px solid var(--border)', background: 'var(--surface-2)', color: 'var(--text-muted)', cursor: 'pointer' }}>‹</button>
        <button onClick={() => scroll(1)} aria-label="Scroll right" style={{ position: 'absolute', right: -12, top: '50%', transform: 'translateY(-50%)', zIndex: 3, width: 26, height: 26, borderRadius: '50%', border: '1px solid var(--border)', background: 'var(--surface-2)', color: 'var(--text-muted)', cursor: 'pointer' }}>›</button>
        <div ref={rowRef} style={{ display: 'flex', gap: 10, overflowX: 'auto', scrollBehavior: 'smooth', paddingBottom: 4, alignItems: 'flex-start' }}>
          {items.map(item => renderCard(item))}
        </div>
      </div>
    </div>
  );
}
