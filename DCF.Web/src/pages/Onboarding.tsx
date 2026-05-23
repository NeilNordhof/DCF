import { useAuth0 } from '@auth0/auth0-react';
import { useState } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useUser } from '../context/UserContext';

export function Onboarding() {
  const { user: auth0User } = useAuth0();
  const { user, setUser } = useUser();
  const navigate = useNavigate();
  const [displayName, setDisplayName] = useState(auth0User?.name ?? '');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (user) {
    return <Navigate to="/" replace />;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      const profile = await api.upsertUser(displayName);
      setUser(profile);
      navigate('/');
    } catch {
      setError('Failed to create profile. Please try again.');
      setSubmitting(false);
    }
  }

  return (
    <div>
      <h1>Welcome to DCF Fantasy!</h1>
      <form onSubmit={handleSubmit}>
        <label>
          Display name
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            required
            minLength={1}
          />
        </label>
        {error && <div>{error}</div>}
        <button type="submit" disabled={submitting}>
          {submitting ? 'Creating...' : 'Continue'}
        </button>
      </form>
    </div>
  );
}
