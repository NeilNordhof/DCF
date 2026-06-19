import { Auth0Provider, useAuth0 } from '@auth0/auth0-react';
import { createContext, useContext } from 'react';
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

function Auth0Bridge({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading, user, logout, getAccessTokenSilently, loginWithRedirect } = useAuth0();

  const value: AuthValue = {
    isAuthenticated,
    isLoading,
    user: user ? { name: user.name ?? '', email: user.email ?? '' } : null,
    logout: () => logout({ logoutParams: { returnTo: window.location.origin } }),
    getAccessTokenSilently: () => getAccessTokenSilently(),
    loginWithRedirect: () => loginWithRedirect(),
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

  return (
    <Auth0Provider
      domain={import.meta.env.VITE_AUTH0_DOMAIN}
      clientId={import.meta.env.VITE_AUTH0_CLIENT_ID}
      authorizationParams={{
        redirect_uri: window.location.origin,
        audience: import.meta.env.VITE_AUTH0_AUDIENCE,
      }}
    >
      <Auth0Bridge>{children}</Auth0Bridge>
    </Auth0Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be inside AuthProvider');
  return ctx;
}
