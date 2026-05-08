import { delay, http, HttpResponse } from 'msw';
import { Route, Routes, useLocation } from 'react-router-dom';
import { describe, expect, it } from 'vitest';
import userEvent from '@testing-library/user-event';
import { screen, waitFor } from '@testing-library/react';
import { LoginForm } from '../auth/LoginForm';
import { RequireAuth } from '../auth/RequireAuth';
import { server } from '../test/msw';
import { renderWithProviders } from '../test/render';

describe('auth UI', () => {
  it('LoginForm_OnInvalidCredentials_StaysMounted_ShowsServerError', async () => {
    const user = userEvent.setup();

    server.use(
      http.post('/api/auth/login', () =>
        HttpResponse.json(
          problemDetails({
            errors: {
              email: ['Email or password is incorrect.'],
            },
            status: 400,
          }),
          { status: 400 },
        ),
      ),
    );

    renderWithProviders(
      <Routes>
        <Route path="/login" element={<LoginForm />} />
      </Routes>,
      { route: '/login' },
    );

    await user.type(screen.getByLabelText(/email/i), 'demo@example.com');
    await user.type(screen.getByLabelText('Password'), 'bad-password');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByText('Email or password is incorrect.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /sign in/i })).toBeInTheDocument();
  });

  it('LoginForm_OnSuccess_NavigatesToReturnTo', async () => {
    const user = userEvent.setup();

    server.use(http.post('/api/auth/login', () => HttpResponse.json(authUser())));

    renderWithProviders(
      <>
        <Routes>
          <Route path="/login" element={<LoginForm />} />
          <Route path="/todos" element={<p>Todos route</p>} />
        </Routes>
        <LocationProbe />
      </>,
      { route: '/login?returnTo=/todos' },
    );

    await user.type(screen.getByLabelText(/email/i), 'demo@example.com');
    await user.type(screen.getByLabelText('Password'), 'Demo123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByText('Todos route')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/todos');
  });

  it('LoginForm_OnSuccess_RejectsUnsafeReturnToValues', async () => {
    const user = userEvent.setup();
    server.use(http.post('/api/auth/login', () => HttpResponse.json(authUser())));

    renderWithProviders(
      <>
        <Routes>
          <Route path="/login" element={<LoginForm />} />
          <Route path="/" element={<p>Home route</p>} />
        </Routes>
        <LocationProbe />
      </>,
      { route: '/login?returnTo=%2F%2Fevil.example&returnTo=/todos' },
    );

    await user.type(screen.getByLabelText(/email/i), 'demo@example.com');
    await user.type(screen.getByLabelText('Password'), 'Demo123!');
    await user.click(screen.getByRole('button', { name: /sign in/i }));

    expect(await screen.findByText('Home route')).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/');
  });

  it('RequireAuth_WhileLoading_DoesNotRedirect', async () => {
    server.use(
      http.get('/api/auth/me', async () => {
        await delay(150);
        return HttpResponse.json(authUser());
      }),
    );

    renderWithProviders(
      <>
        <Routes>
          <Route element={<RequireAuth />}>
            <Route path="/" element={<p>Protected route</p>} />
          </Route>
          <Route path="/login" element={<p>Login route</p>} />
        </Routes>
        <LocationProbe />
      </>,
      { route: '/' },
    );

    expect(screen.getByRole('status')).toHaveTextContent('Checking session...');
    expect(screen.queryByText('Login route')).not.toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent('/');

    await waitFor(() => expect(screen.getByText('Protected route')).toBeInTheDocument());
  });

  it('RequireAuth_RedirectsWithEncodedReturnTo', async () => {
    server.use(
      http.get('/api/auth/me', () =>
        HttpResponse.json(problemDetails({ status: 401 }), { status: 401 }),
      ),
    );

    renderWithProviders(
      <>
        <Routes>
          <Route element={<RequireAuth />}>
            <Route path="/todos" element={<p>Protected route</p>} />
          </Route>
          <Route path="/login" element={<p>Login route</p>} />
        </Routes>
        <LocationProbe />
      </>,
      { route: '/todos?search=a b&status=DueToday#row-1' },
    );

    await waitFor(() => expect(screen.getByText('Login route')).toBeInTheDocument());
    expect(screen.getByTestId('location')).toHaveTextContent(
      '/login?returnTo=%2Ftodos%3Fsearch%3Da%20b%26status%3DDueToday%23row-1',
    );
  });
});

function LocationProbe() {
  const location = useLocation();

  return (
    <div data-testid="location">
      {location.pathname}
      {location.search}
    </div>
  );
}

function authUser() {
  return {
    id: '11111111-1111-4111-8111-111111111111',
    email: 'demo@example.com',
    role: 'Basic',
  };
}

function problemDetails({ errors, status }: { errors?: Record<string, string[]>; status: number }) {
  return {
    type: 'about:blank',
    title: 'Request failed.',
    status,
    detail: 'Bad request.',
    instance: '/api/auth/login',
    traceId: 'trace-test',
    ...(errors ? { errors } : {}),
  };
}
