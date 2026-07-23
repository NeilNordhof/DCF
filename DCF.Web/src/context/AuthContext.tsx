import { Auth0LockPasswordless } from 'auth0-lock';
import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { api } from '../api/client';
import { REMEMBER_TOKEN_STORAGE_KEY, resolveAccessToken, resolveSession } from './authSession';
import { DevAuthProvider, useDevAuth } from './DevAuthContext';

export interface AuthValue {
  isAuthenticated: boolean;
  isLoading: boolean;
  user: { name: string; email: string } | null;
  logout: () => void;
  getAccessTokenSilently: () => Promise<string>;
  loginWithRedirect: () => void;
  devLogin?: (sub: string) => void;
}

const AuthContext = createContext<AuthValue | null>(null);

const TOKEN_KEY = 'dcf_access_token';
const TOKEN_EXPIRY_KEY = 'dcf_token_expiry';
const USER_KEY = 'dcf_user';

function readStoredSession() {
  const expiryStr = localStorage.getItem(TOKEN_EXPIRY_KEY);
  const userStr = localStorage.getItem(USER_KEY);

  return resolveSession(
    {
      accessToken: localStorage.getItem(TOKEN_KEY),
      tokenExpiry: expiryStr ? parseInt(expiryStr, 10) : null,
      rememberToken: localStorage.getItem(REMEMBER_TOKEN_STORAGE_KEY),
      user: userStr ? (JSON.parse(userStr) as { name: string; email: string }) : null,
    },
    Date.now()
  );
}

function DevAuthBridge({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading, user, logout, getAccessTokenSilently, login } = useDevAuth();

  const value: AuthValue = {
    isAuthenticated,
    isLoading,
    user,
    logout,
    getAccessTokenSilently,
    loginWithRedirect: () => {},
    devLogin: login,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

function ProductionLockProvider({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState(() => {
    const session = readStoredSession();

    return {
      isAuthenticated: session.isAuthenticated,
      isLoading: false,
      user: session.user,
    };
  });

  const lockRef = useRef<InstanceType<typeof Auth0LockPasswordless> | null>(null);

  useEffect(() => {
    const lock = new Auth0LockPasswordless(
      import.meta.env.VITE_AUTH0_CLIENT_ID,
      import.meta.env.VITE_AUTH0_DOMAIN,
      {
        container: 'auth0-lock-container',
        passwordlessMethod: 'code',
        allowedConnections: ['email', 'google-oauth2'],
        closable: false,
        avatar: null,
        auth: {
          responseType: 'token id_token',
          audience: import.meta.env.VITE_AUTH0_AUDIENCE,
          redirect: false,
          params: { scope: 'openid profile email' },
        },
        socialButtonStyle: 'big',
        languageDictionary: { title: '' },
        theme: {
          logo: '',
          primaryColor: '#c084fc',
          hideMainScreenTitle: true,
        },
      },
    );

    lock.on('authenticated', (authResult) => {
      // Decode id_token directly — getUserInfo fails when a custom audience is set
      // because the access_token is scoped to our API, not Auth0's /userinfo endpoint
      const b64 = authResult.idToken.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
      const claims = JSON.parse(atob(b64)) as Record<string, string>;
      const expiry = Date.now() + authResult.expiresIn * 1000;
      const user = { name: claims['name'] ?? claims['email'] ?? '', email: claims['email'] ?? '' };

      localStorage.setItem(TOKEN_KEY, authResult.accessToken);
      localStorage.setItem(TOKEN_EXPIRY_KEY, String(expiry));
      localStorage.setItem(USER_KEY, JSON.stringify(user));

      setState({ isAuthenticated: true, isLoading: false, user });

      api.issueRememberMeToken()
        .then(({ token }) => localStorage.setItem(REMEMBER_TOKEN_STORAGE_KEY, token))
        .catch((err) => console.error('Failed to issue remember-me token:', err));
    });

    lockRef.current = lock;

    if (!readStoredSession().isAuthenticated && document.getElementById('auth0-lock-container')) {
      lock.show();
    }
  }, []);

  const showLock = useCallback(() => {
    lockRef.current?.show();
  }, []);

  const logout = useCallback(() => {
    const rememberToken = localStorage.getItem(REMEMBER_TOKEN_STORAGE_KEY);

    api.logout(rememberToken).catch((err) => console.error('Failed to revoke remember-me token:', err));

    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(TOKEN_EXPIRY_KEY);
    localStorage.removeItem(USER_KEY);
    localStorage.removeItem(REMEMBER_TOKEN_STORAGE_KEY);
    setState({ isAuthenticated: false, isLoading: false, user: null });
  }, []);

  const getAccessTokenSilently = useCallback((): Promise<string> => {
    return Promise.resolve(resolveAccessToken(readStoredSession()));
  }, []);

  const value: AuthValue = {
    isAuthenticated: state.isAuthenticated,
    isLoading: state.isLoading,
    user: state.user,
    logout,
    getAccessTokenSilently,
    loginWithRedirect: showLock,
  };

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  if (import.meta.env.DEV) {
    return (
      <DevAuthProvider>
        <DevAuthBridge>{children}</DevAuthBridge>
      </DevAuthProvider>
    );
  }

  return <ProductionLockProvider>{children}</ProductionLockProvider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be inside AuthProvider');
  return ctx;
}
