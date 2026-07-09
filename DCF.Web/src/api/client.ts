import type { ActiveSeason, Corps, CreateLeagueRequest, League, MemberScoreBreakdown, PublicLeague, Season, SeasonCorps, SeasonDetail, Show, ShowPrefillResponse, ShowScheduleEntry, Standing, TriggerScrapeResult, UpdateLeagueRequest, UserProfile } from '../types/api';
import { REMEMBER_TOKEN_STORAGE_KEY } from '../context/authSession';

const API_URL = import.meta.env.VITE_API_URL as string;

export class AuthExpiredError extends Error {}

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
  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

export const api = {
  getUser: async (): Promise<UserProfile | null> => {
    const token = getToken ? await getToken() : null;
    const rememberToken = localStorage.getItem(REMEMBER_TOKEN_STORAGE_KEY);
    const res = await fetch(`${API_URL}/api/auth/me`, {
      headers: {
        'Content-Type': 'application/json',
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(rememberToken ? { 'X-Remember-Token': rememberToken } : {}),
      },
    });

    if (res.status === 404) return null;
    if (res.status === 401) throw new AuthExpiredError('Session expired');
    if (!res.ok) throw new Error(await res.text());

    return res.json() as Promise<UserProfile>;
  },
  upsertUser: (displayName: string, email: string) =>
    request<UserProfile>('/api/auth/me', { method: 'POST', body: JSON.stringify({ displayName, email }) }),
  unsubscribe: (token: string) =>
    request<void>('/api/notifications/unsubscribe', { method: 'POST', body: JSON.stringify({ token }) }),
  updateNotificationPreferences: (emailNotificationsEnabled: boolean) =>
    request<void>('/api/notifications/preferences', { method: 'PATCH', body: JSON.stringify({ emailNotificationsEnabled }) }),
  issueRememberMeToken: (): Promise<{ token: string }> =>
    request<{ token: string }>('/api/auth/remember-me', { method: 'POST' }),
  logout: (rememberToken: string | null) =>
    request<void>('/api/auth/logout', { method: 'POST', body: JSON.stringify({ rememberToken }) }),
  getLeagues: () => request<League[]>('/api/leagues'),
  getLeague: (id: string, code?: string) => {
    const params = code ? `?code=${encodeURIComponent(code)}` : '';
    return request<League>(`/api/leagues/${id}${params}`);
  },
  getPublicLeagues: () => request<PublicLeague[]>('/api/leagues/public'),
  lookupLeagueByCode: (code: string) =>
    request<{ leagueId: string }>(`/api/leagues/lookup?code=${encodeURIComponent(code)}`),
  getActiveSeason: () => request<ActiveSeason>('/api/seasons/active'),
  createLeague: (body: CreateLeagueRequest) => request<{ id: string; name: string; inviteCode: string }>('/api/leagues', { method: 'POST', body: JSON.stringify(body) }),
  updateLeague: (id: string, body: UpdateLeagueRequest) =>
    request<void>(`/api/leagues/${id}`, { method: 'PATCH', body: JSON.stringify(body) }),
  refreshInviteCode: (id: string) =>
    request<{ inviteCode: string }>(`/api/leagues/${id}/invite/refresh`, { method: 'POST' }),
  joinLeague: (id: string, inviteCode?: string) =>
    request<void>(`/api/leagues/${id}/join`, { method: 'POST', body: JSON.stringify({ inviteCode }) }),
  getStandings: (id: string) => request<Standing[]>(`/api/leagues/${id}/standings`),
  getScoreBreakdown: (id: string) => request<MemberScoreBreakdown[]>(`/api/leagues/${id}/standings/breakdown`),
  startDraft: (leagueId: string) =>
    request<void>(`/api/leagues/${leagueId}/draft/start`, { method: 'POST' }),
  openDraft: (leagueId: string) =>
    request<void>(`/api/leagues/${leagueId}/draft/open`, { method: 'POST' }),
  submitPick: (leagueId: string, corpsId: string, caption: string) =>
    request<{ id: string; pickNumber: number }>(`/api/leagues/${leagueId}/draft/pick`, {
      method: 'POST', body: JSON.stringify({ corpsId, caption }),
    }),
  skipPick: (leagueId: string) =>
    request<void>(`/api/leagues/${leagueId}/draft/skip`, { method: 'POST' }),
  adminGetCorps: () => request<Corps[]>('/api/admin/corps'),
  adminCreateCorps: (name: string) =>
    request<Corps>('/api/admin/corps', { method: 'POST', body: JSON.stringify({ name }) }),
  adminRenameCorps: (id: string, name: string) =>
    request<Corps>(`/api/admin/corps/${id}`, { method: 'PATCH', body: JSON.stringify({ name }) }),
  adminDeleteCorps: (id: string) =>
    request<void>(`/api/admin/corps/${id}`, { method: 'DELETE' }),
  adminUploadCorpsIcon: async (id: string, file: File): Promise<{ iconUrl: string }> => {
    const token = getToken ? await getToken() : null;
    const form = new FormData();
    form.append('file', file);
    const res = await fetch(`${API_URL}/api/admin/corps/${id}/icon`, {
      method: 'POST',
      headers: token ? { Authorization: `Bearer ${token}` } : {},
      body: form,
    });
    if (!res.ok) throw new Error(await res.text());
    return res.json() as Promise<{ iconUrl: string }>;
  },
  adminTriggerScrape: (showId: string) =>
    request<TriggerScrapeResult>(`/api/admin/shows/${showId}/scrape`, { method: 'POST' }),
  adminGetSeasons: () =>
    request<Season[]>('/api/admin/seasons'),
  adminGetSeason: (id: string) =>
    request<SeasonDetail>(`/api/admin/seasons/${id}`),
  adminCreateSeason: (year: number, startDate: string, endDate: string) =>
    request<Season>('/api/admin/seasons', { method: 'POST', body: JSON.stringify({ year, startDate, endDate }) }),
  adminPublishSeason: (id: string) =>
    request<void>(`/api/admin/seasons/${id}/publish`, { method: 'POST' }),
  adminSetSeasonCorps: (id: string, corpsIds: string[]) =>
    request<void>(`/api/admin/seasons/${id}/corps`, { method: 'PUT', body: JSON.stringify({ corpsIds }) }),
  adminSetCorpsOrder: (seasonId: string, orders: { corpsId: string; sortOrder: number | null }[]) =>
    request<void>(`/api/admin/seasons/${seasonId}/corps/order`, { method: 'PUT', body: JSON.stringify({ orders }) }),
  getSeasonCorps: (seasonId: string) =>
    request<SeasonCorps[]>(`/api/seasons/${seasonId}/corps`),
  adminGetShows: (seasonId: string) =>
    request<Show[]>(`/api/admin/seasons/${seasonId}/shows`),
  adminCreateShow: (
    seasonId: string,
    body: {
      name: string;
      url?: string | null;
      date: string;
      startTime: string | null;
      scoresAnnouncedTime: string | null;
      timezone?: string;
      isExhibition: boolean;
      location?: string | null;
      latitude?: number | null;
      longitude?: number | null;
      corpsIds: string[];
      schedule: ShowScheduleEntry[];
    }
  ) =>
    request<{ id: string; name: string }>(`/api/admin/seasons/${seasonId}/shows`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  adminUpdateSeasonDates: (id: string, startDate: string, endDate: string) =>
    request<void>(`/api/admin/seasons/${id}/dates`, { method: 'PATCH', body: JSON.stringify({ startDate, endDate }) }),
  adminUpdateShow: (
    id: string,
    body: {
      name: string;
      url?: string | null;
      date: string;
      startTime: string | null;
      scoresAnnouncedTime: string | null;
      timezone?: string;
      isExhibition: boolean;
      location?: string | null;
      latitude?: number | null;
      longitude?: number | null;
      corpsIds: string[];
      schedule: ShowScheduleEntry[];
    }
  ) =>
    request<void>(`/api/admin/shows/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  adminDeleteShow: (id: string) =>
    request<void>(`/api/admin/shows/${id}`, { method: 'DELETE' }),
  adminSetNoScoreReason: (id: string, reason: string | null) =>
    request<void>(`/api/admin/shows/${id}/no-score-reason`, { method: 'PATCH', body: JSON.stringify({ reason }) }),
  adminPrefillShow: (seasonId: string, name: string) =>
    request<ShowPrefillResponse>(
      `/api/admin/seasons/${seasonId}/shows/prefill?name=${encodeURIComponent(name)}`
    ),
};
