import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { useQueryClient } from '@tanstack/react-query';
import ActionButton from '../components/ui/ActionButton';
import Card from '../components/ui/Card';
import { TextInput } from '../components/ui/TextInput';
import { ApiProblem } from '../lib/api';
import { applyApiProblemToForm } from './formErrors';
import { getMe } from './api';
import { authKeys } from './queryKeys';
import { RegisterRequestSchema, type RegisterRequest } from './schemas';
import { useRegister } from './useAuth';
import styles from './AuthForm.module.css';

const ACKNOWLEDGEMENT_MESSAGE =
  'Registration request received. If your email was new, you can sign in now.';

export function RegisterForm() {
  const [formError, setFormError] = useState<string>();
  const [acknowledgement, setAcknowledgement] = useState<string>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const registerAccount = useRegister();
  const queryClient = useQueryClient();
  const {
    formState: { errors, isSubmitting },
    handleSubmit,
    register,
    setError,
  } = useForm<RegisterRequest>({
    defaultValues: {
      email: '',
      password: '',
    },
    resolver: zodResolver(RegisterRequestSchema),
  });

  async function onSubmit(values: RegisterRequest) {
    setFormError(undefined);
    setAcknowledgement(undefined);

    try {
      // The register response is uniform across both branches. Probe /me
      // directly: if it returns a user, registration created the account and
      // we navigate. If it 401s, the email was already registered, so show the
      // generic acknowledgement and stay on the form.
      await registerAccount.mutateAsync(values);

      try {
        const me = await queryClient.fetchQuery({
          queryKey: authKeys.me(),
          queryFn: getMe,
          staleTime: 0,
        });
        if (me?.id) {
          navigate(safeReturnTo(searchParams.get('returnTo')), { replace: true });
          return;
        }
      } catch (probeError) {
        if (probeError instanceof ApiProblem && probeError.status === 401) {
          setAcknowledgement(ACKNOWLEDGEMENT_MESSAGE);
          return;
        }

        throw probeError;
      }

      // Defensive: /me returned without throwing but produced no user. Treat
      // as the duplicate-email branch.
      setAcknowledgement(ACKNOWLEDGEMENT_MESSAGE);
    } catch (error) {
      const handled = applyApiProblemToForm<RegisterRequest>(
        error,
        ['email', 'password'],
        setError,
        setFormError,
      );

      if (!handled) {
        throw error;
      }
    }
  }

  const busy = isSubmitting || registerAccount.isPending;

  return (
    <Card as="section" className={styles.shell}>
      <header className={styles.header}>
        <h1 className={styles.title}>Create Account</h1>
        <p className={styles.caption}>ACCESS // SECURE LINK</p>
        <p className={styles.subtitle}>Register to start using the todo app.</p>
      </header>

      <form className={styles.form} onSubmit={handleSubmit(onSubmit)} noValidate>
        {formError ? (
          <p className={styles.formError} role="alert">
            {formError}
          </p>
        ) : null}

        {acknowledgement ? (
          <p className={styles.formError} role="status" data-testid="register-acknowledgement">
            {acknowledgement}
          </p>
        ) : null}

        <TextInput
          autoComplete="email"
          error={errors.email?.message}
          id="register-email"
          label="Email"
          type="email"
          {...register('email')}
        />

        <TextInput
          autoComplete="new-password"
          error={errors.password?.message}
          id="register-password"
          label="Password"
          type="password"
          {...register('password')}
        />

        <div className={styles.actions}>
          <ActionButton aria-busy={busy} disabled={busy} type="submit" variant="primary">
            Register
          </ActionButton>

          <div className={styles.switchRow}>
            <p className={styles.switchLabel}>ALREADY REGISTERED?</p>
            <ActionButton
              onClick={() => navigate('/login')}
              size="sm"
              type="button"
              variant="neutral"
            >
              Sign in
            </ActionButton>
          </div>
        </div>
      </form>
    </Card>
  );
}

function safeReturnTo(value: string | null) {
  if (!value || !value.startsWith('/') || value.startsWith('//')) {
    return '/';
  }

  return value;
}
