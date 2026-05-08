import { useState } from 'react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import ActionButton from '../components/ui/ActionButton';
import Card from '../components/ui/Card';
import { TextInput } from '../components/ui/TextInput';
import { applyApiProblemToForm } from './formErrors';
import { LoginRequestSchema, type LoginRequest } from './schemas';
import { useLogin } from './useAuth';
import styles from './AuthForm.module.css';

export function LoginForm() {
  const [formError, setFormError] = useState<string>();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const login = useLogin();
  const {
    formState: { errors, isSubmitting },
    handleSubmit,
    register,
    setError,
  } = useForm<LoginRequest>({
    defaultValues: {
      email: '',
      password: '',
    },
    resolver: zodResolver(LoginRequestSchema),
  });

  async function onSubmit(values: LoginRequest) {
    setFormError(undefined);

    try {
      await login.mutateAsync(values);
      navigate(safeReturnTo(searchParams.getAll('returnTo')), { replace: true });
    } catch (error) {
      const handled = applyApiProblemToForm<LoginRequest>(
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

  const busy = isSubmitting || login.isPending;

  return (
    <Card as="section" className={styles.shell}>
      <header className={styles.header}>
        <h1 className={styles.title}>Todo App</h1>
        <p className={styles.caption}>ACCESS // SECURE LINK</p>
        <p className={styles.subtitle}>Sign in to continue.</p>
      </header>

      <form className={styles.form} onSubmit={handleSubmit(onSubmit)} noValidate>
        {formError ? (
          <p className={styles.formError} role="alert">
            {formError}
          </p>
        ) : null}

        <TextInput
          autoComplete="email"
          error={errors.email?.message}
          id="login-email"
          label="Email"
          type="email"
          {...register('email')}
        />

        <TextInput
          autoComplete="current-password"
          error={errors.password?.message}
          id="login-password"
          label="Password"
          type="password"
          {...register('password')}
        />

        <div className={styles.actions}>
          <ActionButton aria-busy={busy} disabled={busy} type="submit" variant="primary">
            Sign in
          </ActionButton>

          <div className={styles.switchRow}>
            <p className={styles.switchLabel}>NEED AN ACCOUNT?</p>
            <ActionButton
              onClick={() => navigate('/register')}
              size="sm"
              type="button"
              variant="neutral"
            >
              Register
            </ActionButton>
          </div>
        </div>
      </form>
    </Card>
  );
}

function safeReturnTo(values: string[]) {
  if (values.length !== 1) {
    return '/';
  }

  const [value] = values;
  if (
    !value ||
    !value.startsWith('/') ||
    value.startsWith('//') ||
    containsUnsafeReturnToChar(value)
  ) {
    return '/';
  }

  return value;
}

function containsUnsafeReturnToChar(value: string) {
  for (const char of value) {
    const code = char.charCodeAt(0);
    if (code <= 0x1f || code === 0x7f || char === '\\') {
      return true;
    }
  }

  return false;
}
