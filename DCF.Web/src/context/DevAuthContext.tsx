import { createContext, useCallback, useContext, useRef, useState } from 'react';

export const DEV_USERS = [
  { sub: 'dev|alice', displayName: 'Alice', email: 'alice@dev.local' },
  { sub: 'dev|bob', displayName: 'Bob', email: 'bob@dev.local' },
  { sub: 'dev|charlie', displayName: 'Charlie', email: 'charlie@dev.local' },
  { sub : 'dev|dave', displayName: 'Dave', email: 'dave@dev.local' },
  { sub : 'dev|neil', displayName: 'Neil', email: 'neil@dev.local' },
  { sub : 'dev|olivia', displayName: 'Olivia', email: 'olivia@dev.local' },
  { sub : 'dev|peggy', displayName: 'Peggy', email: 'peggy@dev.local' },
  { sub : 'dev|trent', displayName: 'Trent', email: 'trent@dev.local' },
  { sub : 'dev|victor', displayName: 'Victor', email: 'victor@dev.local' },
  { sub : 'dev|wendy', displayName: 'Wendy', email: 'wendy@dev.local' }
];

interface DevUser {
  sub: string;
  displayName: string;
  email: string;
}

interface DevAuthValue {
  isAuthenticated: boolean;
  isLoading: boolean;
  user: { name: string; email: string } | null;
  login: (sub: string) => void;
  logout: () => void;
  getAccessTokenSilently: () => Promise<string>;
}

const STORAGE_KEY = 'dcf_dev_user';

const DevAuthContext = createContext<DevAuthValue | null>(null);

export function DevAuthProvider({ children }: { children: React.ReactNode }) {
  const [currentUser, setCurrentUser] = useState<DevUser | null>(() => {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored ? (DEV_USERS.find(u => u.sub === stored) ?? null) : null;
  });

  const currentUserRef = useRef(currentUser);
  currentUserRef.current = currentUser;

  function login(sub: string) {
    const user = DEV_USERS.find(u => u.sub === sub);
    if (!user) return;
    localStorage.setItem(STORAGE_KEY, sub);
    setCurrentUser(user);
  }

  function logout() {
    localStorage.removeItem(STORAGE_KEY);
    setCurrentUser(null);
  }

  const getAccessTokenSilently = useCallback((): Promise<string> => {
    return Promise.resolve(currentUserRef.current?.sub ?? '');
  }, []);

  const value: DevAuthValue = {
    isAuthenticated: currentUser !== null,
    isLoading: false,
    user: currentUser ? { name: currentUser.displayName, email: currentUser.email } : null,
    login,
    logout,
    getAccessTokenSilently,
  };

  return (
    <DevAuthContext.Provider value={value}>
      {children}
    </DevAuthContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useDevAuth(): DevAuthValue {
  const ctx = useContext(DevAuthContext);
  if (!ctx) throw new Error('useDevAuth must be inside DevAuthProvider');
  return ctx;
}
