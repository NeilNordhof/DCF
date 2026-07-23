export const REMEMBER_TOKEN_STORAGE_KEY = 'dcf_remember_token';

export interface StoredAuthState {
  accessToken: string | null;
  tokenExpiry: number | null;
  rememberToken: string | null;
  user: { name: string; email: string } | null;
}

export interface ResolvedSession {
  isAuthenticated: boolean;
  user: { name: string; email: string } | null;
  bearerToken: string | null;
}

export function resolveSession(state: StoredAuthState, now: number): ResolvedSession {
  const accessTokenValid = state.tokenExpiry !== null && now < state.tokenExpiry && !!state.accessToken;

  if (accessTokenValid) {
    return { isAuthenticated: true, user: state.user, bearerToken: state.accessToken };
  }

  if (state.rememberToken) {
    return { isAuthenticated: true, user: state.user, bearerToken: state.rememberToken };
  }

  return { isAuthenticated: false, user: null, bearerToken: null };
}

export function resolveAccessToken(session: ResolvedSession): string {
  return session.bearerToken ?? '';
}
