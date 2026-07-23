import { useEffect, useRef, useState } from 'react';
import type { MouseEvent as ReactMouseEvent, ReactNode } from 'react';

const DRAG_THRESHOLD_PX = 5;

export function WeekRow<T>({ weekLabel, items, renderCard }: { weekLabel: string; items: T[]; renderCard: (item: T) => ReactNode }) {
  const rowRef = useRef<HTMLDivElement>(null);
  const dragOriginRef = useRef<{ startX: number; startScrollLeft: number } | null>(null);
  const draggedRef = useRef(false);
  const [isDragging, setIsDragging] = useState(false);

  function scroll(dir: 1 | -1) {
    rowRef.current?.scrollBy({ left: dir * 250, behavior: 'smooth' });
  }

  function handleMouseDown(e: ReactMouseEvent<HTMLDivElement>) {
    if (e.button !== 0 || !rowRef.current) {
      return;
    }

    dragOriginRef.current = { startX: e.clientX, startScrollLeft: rowRef.current.scrollLeft };
    draggedRef.current = false;
    setIsDragging(true);
  }

  useEffect(() => {
    if (!isDragging) {
      return;
    }

    function handleMouseMove(e: MouseEvent) {
      const origin = dragOriginRef.current;

      if (!origin || !rowRef.current) {
        return;
      }

      const delta = e.clientX - origin.startX;

      if (Math.abs(delta) > DRAG_THRESHOLD_PX) {
        draggedRef.current = true;
      }

      rowRef.current.scrollLeft = origin.startScrollLeft - delta;
    }

    function handleMouseUp() {
      setIsDragging(false);
      dragOriginRef.current = null;
    }

    window.addEventListener('mousemove', handleMouseMove);
    window.addEventListener('mouseup', handleMouseUp);

    return () => {
      window.removeEventListener('mousemove', handleMouseMove);
      window.removeEventListener('mouseup', handleMouseUp);
    };
  }, [isDragging]);

  function handleClickCapture(e: ReactMouseEvent<HTMLDivElement>) {
    if (draggedRef.current) {
      e.preventDefault();
      e.stopPropagation();
    }
  }

  return (
    <div>
      <div style={{ fontSize: 13, fontWeight: 700, color: 'var(--text-heading)', margin: '22px 0 10px' }}>
        {weekLabel} <span style={{ fontSize: 11, fontWeight: 600, color: 'var(--text-muted)', marginLeft: 6 }}>{items.length} show{items.length === 1 ? '' : 's'}</span>
      </div>
      <div style={{ position: 'relative' }}>
        <button onClick={() => scroll(-1)} aria-label="Scroll left" style={{ position: 'absolute', left: -12, top: '50%', transform: 'translateY(-50%)', zIndex: 3, width: 26, height: 26, borderRadius: '50%', border: '1px solid var(--border)', background: 'var(--surface-2)', color: 'var(--text-muted)', cursor: 'pointer' }}>‹</button>
        <button onClick={() => scroll(1)} aria-label="Scroll right" style={{ position: 'absolute', right: -12, top: '50%', transform: 'translateY(-50%)', zIndex: 3, width: 26, height: 26, borderRadius: '50%', border: '1px solid var(--border)', background: 'var(--surface-2)', color: 'var(--text-muted)', cursor: 'pointer' }}>›</button>
        <div
          ref={rowRef}
          onMouseDown={handleMouseDown}
          onClickCapture={handleClickCapture}
          style={{
            display: 'flex',
            gap: 10,
            overflowX: 'auto',
            scrollBehavior: 'smooth',
            paddingBottom: 4,
            alignItems: 'flex-start',
            cursor: isDragging ? 'grabbing' : 'grab',
            userSelect: isDragging ? 'none' : undefined,
          }}
        >
          {items.map(item => renderCard(item))}
        </div>
      </div>
    </div>
  );
}
