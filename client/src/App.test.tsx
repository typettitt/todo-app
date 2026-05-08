import { render, screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { describe, it, expect } from 'vitest';
import App from './App';
import { server } from './test/msw';

describe('App', () => {
  it('renders the protected app shell for a signed-in user', async () => {
    window.history.pushState(null, '', '/');
    server.use(
      http.get('/api/auth/me', () =>
        HttpResponse.json({
          id: '11111111-1111-4111-8111-111111111111',
          email: 'demo@example.com',
          role: 'Basic',
        }),
      ),
      http.get('/api/todos', () =>
        HttpResponse.json({
          items: [],
          page: 1,
          pageSize: 20,
          total: 0,
          hasNext: false,
        }),
      ),
    );

    render(<App />);

    expect(
      await screen.findByRole('heading', { name: /todo console/i, level: 1 }),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: /tasks/i, level: 2 })).toBeInTheDocument();
    expect(screen.getByText('demo@example.com')).toBeInTheDocument();
  });
});
