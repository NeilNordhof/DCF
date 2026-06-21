import { useEffect, useState } from 'react';
import type { CSSProperties } from 'react';

interface TimePickerProps {
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
  style?: CSSProperties;
}

function to12(h24: number): { hour: number; ampm: 'AM' | 'PM' }
{
  if (h24 === 0) return { hour: 12, ampm: 'AM' };
  if (h24 < 12) return { hour: h24, ampm: 'AM' };
  if (h24 === 12) return { hour: 12, ampm: 'PM' };
  return { hour: h24 - 12, ampm: 'PM' };
}

function to24(hour: number, ampm: 'AM' | 'PM'): number
{
  if (ampm === 'AM') return hour === 12 ? 0 : hour;
  return hour === 12 ? 12 : hour + 12;
}

function parseValue(v: string): { hour: number; minute: number; ampm: 'AM' | 'PM' } | null
{
  if (!v) return null;
  const parts = v.split(':');
  if (parts.length !== 2) return null;
  const h24 = parseInt(parts[0], 10);
  const m = parseInt(parts[1], 10);
  if (isNaN(h24) || isNaN(m)) return null;
  return { ...to12(h24), minute: m };
}

export function TimePicker({ value, onChange, required = false, style }: TimePickerProps)
{
  const initial = parseValue(value);
  const [hour, setHour] = useState(initial?.hour ?? 12);
  const [minute, setMinute] = useState(initial?.minute ?? 0);
  const [ampm, setAmpm] = useState<'AM' | 'PM'>(initial?.ampm ?? 'PM');
  const [isEmpty, setIsEmpty] = useState(!value);
  const [typedHour, setTypedHour] = useState('');
  const [typedMinute, setTypedMinute] = useState('');
  const [focusedField, setFocusedField] = useState<'hour' | 'minute' | null>(null);

  useEffect(() => {
    if (!value) {
      setIsEmpty(true);
      return;
    }
    const parsed = parseValue(value);
    if (!parsed) return;
    setHour(parsed.hour);
    setMinute(parsed.minute);
    setAmpm(parsed.ampm);
    setIsEmpty(false);
  }, [value]);

  function emit(h: number, m: number, ap: 'AM' | 'PM')
  {
    const h24 = to24(h, ap);
    onChange(`${String(h24).padStart(2, '0')}:${String(m).padStart(2, '0')}`);
  }

  function initDefault()
  {
    setHour(12);
    setMinute(0);
    setAmpm('PM');
    setIsEmpty(false);
    emit(12, 0, 'PM');
  }

  function stepHour(dir: 1 | -1)
  {
    if (isEmpty)
    {
      initDefault();
      return;
    }
    const next = dir === 1 ? (hour === 12 ? 1 : hour + 1) : (hour === 1 ? 12 : hour - 1);
    setHour(next);
    emit(next, minute, ampm);
  }

  function stepMinute(dir: 1 | -1)
  {
    if (isEmpty)
    {
      initDefault();
      return;
    }
    const newMin = minute + dir * 5;

    if (newMin >= 60)
    {
      const newHour = hour === 12 ? 1 : hour + 1;
      setMinute(0);
      setHour(newHour);
      emit(newHour, 0, ampm);
    }
    else if (newMin < 0)
    {
      const newHour = hour === 1 ? 12 : hour - 1;
      setMinute(55);
      setHour(newHour);
      emit(newHour, 55, ampm);
    }
    else
    {
      setMinute(newMin);
      emit(hour, newMin, ampm);
    }
  }

  function commitHour(typed: string)
  {
    setTypedHour('');

    if (typed === '')
    {
      if (!required)
      {
        setIsEmpty(true);
        onChange('');
      }
      return;
    }

    const parsed = parseInt(typed, 10);
    if (isNaN(parsed)) return;
    const clamped = Math.max(1, Math.min(12, parsed));
    setHour(clamped);
    setIsEmpty(false);
    emit(clamped, minute, ampm);
  }

  function commitMinute(typed: string)
  {
    setTypedMinute('');

    if (typed === '') return;
    const parsed = parseInt(typed, 10);
    if (isNaN(parsed)) return;
    const clamped = Math.max(0, Math.min(59, parsed));
    setMinute(clamped);
    emit(hour, clamped, ampm);
  }

  const arrowStyle: CSSProperties = {
    width: 28, height: 20, borderRadius: 3, fontSize: 9, fontWeight: 700,
    background: 'var(--surface)', border: '1px solid var(--border)',
    color: 'var(--text-heading)', cursor: 'pointer',
    display: 'flex', alignItems: 'center', justifyContent: 'center',
    lineHeight: 1, padding: 0,
  };

  const fieldStyle: CSSProperties = {
    width: 30, height: 26, textAlign: 'center', fontSize: 14, fontWeight: 700,
    background: 'transparent', border: 'none', outline: 'none',
    color: isEmpty && focusedField === null ? 'var(--text-faint)' : 'var(--text-heading)',
    padding: 0,
  };

  const hourDisplay = focusedField === 'hour'
    ? typedHour
    : isEmpty
      ? '--'
      : String(hour).padStart(2, '0');

  const minuteDisplay = focusedField === 'minute'
    ? typedMinute
    : isEmpty
      ? '--'
      : String(minute).padStart(2, '0');

  return (
    <div
      style={{
        display: 'inline-flex', alignItems: 'center',
        background: 'var(--bg)', border: '1px solid var(--border-input)',
        borderRadius: 5, padding: '4px 10px', gap: 2,
        ...style,
      }}
    >
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
        <button type="button" tabIndex={-1} style={arrowStyle} onClick={() => stepHour(1)}>▲</button>
        <input
          value={hourDisplay}
          style={fieldStyle}
          onFocus={() => {
            setFocusedField('hour');
            setTypedHour(isEmpty ? '' : String(hour).padStart(2, '0'));
          }}
          onBlur={() => {
            setFocusedField(null);
            commitHour(typedHour);
          }}
          onChange={e => {
            const v = e.target.value.replace(/\D/g, '');
            if (v.length <= 2) setTypedHour(v);
          }}
        />
        <button type="button" tabIndex={-1} style={arrowStyle} onClick={() => stepHour(-1)}>▼</button>
      </div>

      <span style={{ fontSize: 16, fontWeight: 700, color: isEmpty ? 'var(--text-faint)' : 'var(--text-heading)', paddingBottom: 2 }}>:</span>

      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2 }}>
        <button type="button" tabIndex={-1} style={arrowStyle} onClick={() => stepMinute(1)}>▲</button>
        <input
          value={minuteDisplay}
          style={fieldStyle}
          onFocus={() => {
            setFocusedField('minute');
            setTypedMinute(isEmpty ? '' : String(minute).padStart(2, '0'));
          }}
          onBlur={() => {
            setFocusedField(null);
            commitMinute(typedMinute);
          }}
          onChange={e => {
            const v = e.target.value.replace(/\D/g, '');
            if (v.length <= 2) setTypedMinute(v);
          }}
        />
        <button type="button" tabIndex={-1} style={arrowStyle} onClick={() => stepMinute(-1)}>▼</button>
      </div>

      <select
        value={isEmpty ? '' : ampm}
        disabled={isEmpty}
        onChange={e => {
          const ap = e.target.value as 'AM' | 'PM';
          setAmpm(ap);
          emit(hour, minute, ap);
        }}
        style={{
          marginLeft: 6, background: 'transparent', border: 'none', outline: 'none',
          color: isEmpty ? 'var(--text-faint)' : 'var(--text-heading)',
          fontSize: 11, fontWeight: 700,
          cursor: isEmpty ? 'not-allowed' : 'pointer',
        }}
      >
        {isEmpty && <option value="">--</option>}
        <option value="AM">AM</option>
        <option value="PM">PM</option>
      </select>
    </div>
  );
}
