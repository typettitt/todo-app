import type { QueryClient } from '@tanstack/react-query';

/**
 * Drops every query namespaced under the `['private', ...]` prefix. Called at
 * each auth boundary (logout, login success, register success, 401) so that
 * a fresh user never paints with a previous user's cached rows. Cancel before
 * remove so an in-flight private query cannot race a setQueryData with stale
 * data after the cache has already been cleared.
 */
export async function clearPrivateData(queryClient: QueryClient) {
  await queryClient.cancelQueries({ queryKey: ['private'] });
  queryClient.removeQueries({ queryKey: ['private'] });
}
