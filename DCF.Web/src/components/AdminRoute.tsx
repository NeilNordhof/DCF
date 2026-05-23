import { useAuth0 } from '@auth0/auth0-react';
import { Navigate } from 'react-router-dom';
import { useUser } from '../context/UserContext';

export function AdminRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth0();
  const { user } = useUser();

  if (isLoading) return <div>Loading...</div>;
  if (!isAuthenticated) return <Navigate to="/" replace />;
  if (!user) return <div>Loading...</div>;
  if (!user.isAdmin) return <Navigate to="/" replace />;

  return <>{children}</>;
}
