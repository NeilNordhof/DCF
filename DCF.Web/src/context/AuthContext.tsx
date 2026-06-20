import { Auth0LockPasswordless } from 'auth0-lock';
import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
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

function storedTokenValid(): boolean {
  const expiry = localStorage.getItem(TOKEN_EXPIRY_KEY);
  return !!expiry && Date.now() < parseInt(expiry, 10);
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
    const valid = storedTokenValid();
    const userStr = localStorage.getItem(USER_KEY);

    return {
      isAuthenticated: valid,
      isLoading: false,
      user: valid && userStr ? (JSON.parse(userStr) as { name: string; email: string }) : null,
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
      lock.getUserInfo(authResult.accessToken, (err, profile) => {
        if (err) return;

        const expiry = Date.now() + authResult.expiresIn * 1000;
        const user = { name: profile.name ?? profile.email ?? '', email: profile.email ?? '' };

        localStorage.setItem(TOKEN_KEY, authResult.accessToken);
        localStorage.setItem(TOKEN_EXPIRY_KEY, String(expiry));
        localStorage.setItem(USER_KEY, JSON.stringify(user));

        setState({ isAuthenticated: true, isLoading: false, user });
      });
    });

    lockRef.current = lock;

    if (!storedTokenValid() && document.getElementById('auth0-lock-container')) {
      lock.show();
    }
  }, []);

  const showLock = useCallback(() => {
    lockRef.current?.show();
  }, []);

  const logout = useCallback(() => {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(TOKEN_EXPIRY_KEY);
    localStorage.removeItem(USER_KEY);
    setState({ isAuthenticated: false, isLoading: false, user: null });
  }, []);

  const getAccessTokenSilently = useCallback((): Promise<string> => {
    const token = localStorage.getItem(TOKEN_KEY);

    if (token && storedTokenValid()) {
      return Promise.resolve(token);
    }

    return Promise.reject(new Error('Session expired — please sign in again'));
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
