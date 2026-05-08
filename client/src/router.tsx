import type { ReactNode } from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { LoginForm } from './auth/LoginForm';
import { RegisterForm } from './auth/RegisterForm';
import { RequireAuth } from './auth/RequireAuth';
import { TodoListPage } from './todos/TodoListPage';
import styles from './App.module.css';

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<AuthPage form={<LoginForm />} />} />
      <Route path="/register" element={<AuthPage form={<RegisterForm />} />} />
      <Route element={<RequireAuth />}>
        <Route path="/" element={<Navigate to="/todos" replace />} />
        <Route path="/todos" element={<TodoListPage />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}

function AuthPage({ form }: { form: ReactNode }) {
  return <main className={styles.authPage}>{form}</main>;
}
