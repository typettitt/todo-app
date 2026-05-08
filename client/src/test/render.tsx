import type { ReactNode } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { ApiClientBridge } from '../lib/ApiClientBridge';
import { Toaster } from '../lib/Toaster';

export function createTestQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
      mutations: {
        retry: false,
      },
    },
  });
}

export function renderWithProviders(
  ui: ReactNode,
  options: { queryClient?: QueryClient; route?: string } = {},
) {
  const queryClient = options.queryClient ?? createTestQueryClient();
  const route = options.route ?? '/';

  const view = render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <ApiClientBridge />
        {ui}
        <Toaster />
      </MemoryRouter>
    </QueryClientProvider>,
  );

  return {
    queryClient,
    ...view,
  };
}
