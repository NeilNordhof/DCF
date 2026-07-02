import { describe, it, expect } from 'vitest';
import { resolveSession } from './authSession';

const user = { name: 'Alice', email: 'alice@example.com' };
const now = 1_000_000;

describe('resolveSession', () => {
  it('uses the access token when it is still valid', () => {
    const result = resolveSession(
      { accessToken: 'access-1', tokenExpiry: now + 1000, rememberToken: 'remember-1', user },
      now
    );

    expect(result).toEqual({ isAuthenticated: true, user, bearerToken: 'access-1' });
  });

  it('falls back to the remember token when the access token has expired', () => {
    const result = resolveSession(
      { accessToken: 'access-1', tokenExpiry: now - 1000, rememberToken: 'remember-1', user },
      now
    );

    expect(result).toEqual({ isAuthenticated: true, user, bearerToken: 'remember-1' });
  });

  it('falls back to the remember token when there is no access token at all', () => {
    const result = resolveSession(
      { accessToken: null, tokenExpiry: null, rememberToken: 'remember-1', user },
      now
    );

    expect(result).toEqual({ isAuthenticated: true, user, bearerToken: 'remember-1' });
  });

  it('is not authenticated when neither token is valid', () => {
    const result = resolveSession(
      { accessToken: 'access-1', tokenExpiry: now - 1000, rememberToken: null, user },
      now
    );

    expect(result).toEqual({ isAuthenticated: false, user: null, bearerToken: null });
  });

  it('is not authenticated when there is no stored state at all', () => {
    const result = resolveSession(
      { accessToken: null, tokenExpiry: null, rememberToken: null, user: null },
      now
    );

    expect(result).toEqual({ isAuthenticated: false, user: null, bearerToken: null });
  });

  it('falls back to the remember token when tokenExpiry is valid but accessToken is missing (desync)', () => {
    const result = resolveSession(
      { accessToken: null, tokenExpiry: now + 1000, rememberToken: 'remember-1', user },
      now
    );

    expect(result).toEqual({ isAuthenticated: true, user, bearerToken: 'remember-1' });
  });
});
