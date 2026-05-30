import type { CSSProperties } from 'react';

const API_URL = import.meta.env.VITE_API_URL as string;

function getInitials(name: string): string {
  return name
    .replace(/^the\s+/i, '')
    .split(/\s+/)
    .map(w => w[0] ?? '')
    .join('')
    .toUpperCase()
    .slice(0, 3);
}

interface Props {
  name: string;
  iconUrl?: string | null;
  size: number;
  style?: CSSProperties;
}

export function CorpsIcon({ name, iconUrl, size, style }: Props) {
  const base: CSSProperties = {
    width: size,
    height: size,
    borderRadius: 3,
    flexShrink: 0,
    ...style,
  };

  if (iconUrl) {
    return (
      <img
        src={`${API_URL}${iconUrl}`}
        alt={name}
        title={name}
        style={{ ...base, objectFit: 'contain', display: 'block' }}
      />
    );
  }

  return (
    <div
      title={name}
      style={{
        ...base,
        background: '#3a3a4a',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontSize: Math.max(6, Math.floor(size * 0.35)),
        fontWeight: 900,
        color: '#fff',
        letterSpacing: '-0.5px',
        userSelect: 'none',
      }}
    >
      {getInitials(name)}
    </div>
  );
}
