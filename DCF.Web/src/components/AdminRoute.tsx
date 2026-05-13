import { useAuth0 } from '@auth0/auth0-react';
import { useEffect, useState } from 'react';
import { Navigate } from 'react-router-dom';
import { api } from '../api/client';
import type { UserProfile } from '../types/api';

export function AdminRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth0();
  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [checking, setChecking] = useState(true);

  useEffect(() => {
    if (!isAuthenticated) { setChecking(false); return; }
    api.upsertUser()
      .then(p => { setProfile(p); setChecking(false); })
      .catch(() => setChecking(false));
  }, [isAuthenticated]);

  if (isLoading || checking) return <div>Loading...</div>;
  if (!isAuthenticated || !profile?.isAdmin) return <Navigate to="/" replace />;
  return <>{children}</>;
}
