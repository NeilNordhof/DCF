import type { Corps, CreateLeagueRequest, League, Standing, UserProfile } from '../types/api';

const API_URL = import.meta.env.VITE_API_URL as string;

let getToken: (() => Promise<string>) | null = null;

export function setTokenGetter(fn: () => Promise<string>) {
  getToken = fn;
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const token = getToken ? await getToken() : null;
  const res = await fetch(`${API_URL}${path}`, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...options?.headers,
    },
  });
  if (!res.ok) throw new Error(await res.text());
  return res.json() as Promise<T>;
}

export const api = {
  getUser: async (): Promise<UserProfile | null> => {
    const token = getToken ? await getToken() : null;
    const res = await fetch(`${API_URL}/api/auth/me`, {
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
    });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(await res.text());
    return res.json() as Promise<UserProfile>;
  },
  upsertUser: (displayName: string) =>
    request<UserProfile>('/api/auth/me', { method: 'POST', body: JSON.stringify({ displayName }) }),
  getLeagues: () => request<League[]>('/api/leagues'),
  getLeague: (id: string) => request<League>(`/api/leagues/${id}`),
  createLeague: (body: CreateLeagueRequest) => request<{ id: string; name: string; inviteCode: string }>('/api/leagues', { method: 'POST', body: JSON.stringify(body) }),
  joinLeague: (id: string, inviteCode?: string) =>
    request<void>(`/api/leagues/${id}/join`, { method: 'POST', body: JSON.stringify({ inviteCode }) }),
  getStandings: (id: string) => request<Standing[]>(`/api/leagues/${id}/standings`),
  startDraft: (leagueId: string) =>
    request<void>(`/api/leagues/${leagueId}/draft/start`, { method: 'POST' }),
  submitPick: (leagueId: string, corpsId: string, caption: string) =>
    request<{ id: string; pickNumber: number }>(`/api/leagues/${leagueId}/draft/pick`, {
      method: 'POST', body: JSON.stringify({ corpsId, caption }),
    }),
  skipPick: (leagueId: string) =>
    request<void>(`/api/leagues/${leagueId}/draft/skip`, { method: 'POST' }),
  adminGetCorps: () => request<Corps[]>('/api/admin/corps'),
  adminCreateCorps: (name: string) =>
    request<Corps>('/api/admin/corps', { method: 'POST', body: JSON.stringify({ name }) }),
  adminTriggerScrape: (showId: string) =>
    request<void>(`/api/admin/shows/${showId}/scrape`, { method: 'POST' }),
};
