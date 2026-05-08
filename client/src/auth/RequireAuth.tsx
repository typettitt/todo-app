import { useEffect } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Navigate, Outlet, useLocation } from 'react-router-dom';
import { ApiProblem } from '../lib/api';
import { useMe } from './useAuth';
import { clearPrivateData } from './useAuthBoundary';
import styles from '../App.module.css';

export function RequireAuth() {
  const location = useLocation();
  const queryClient = useQueryClient();
  const me = useMe();

  const lostSession = me.isError && (!(me.error instanceof ApiProblem) || me.error.status === 401);

  useEffect(() => {
    if (!lostSession) {
      return;
    }
    void clearPrivateData(queryClient);
  }, [lostSession, queryClient]);

  if (me.isPending) {
    return (
      <main className={styles.page}>
        <p role="status">Checking session...</p>
      </main>
    );
  }

  if (me.isError) {
    if (me.error instanceof ApiProblem && me.error.status !== 401) {
      return (
        <main className={styles.page}>
          <h1>Todo App</h1>
          <p role="alert">Unable to load your session.</p>
        </main>
      );
    }

    const returnTo = location.pathname + location.search + location.hash;
    return <Navigate to={`/login?returnTo=${encodeURIComponent(returnTo || '/')}`} replace />;
  }

  return <Outlet />;
}
