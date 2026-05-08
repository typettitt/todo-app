import { z } from 'zod';
import { apiRequest } from '../lib/api';
import {
  AuthUserSchema,
  RegisterAcknowledgementSchema,
  type LoginRequest,
  type RegisterRequest,
} from './schemas';

export function getMe() {
  return apiRequest('/api/auth/me', {
    method: 'GET',
    skipAuthHandling: true,
    successSchema: AuthUserSchema,
  });
}

export function login(request: LoginRequest) {
  return apiRequest('/api/auth/login', {
    body: request,
    method: 'POST',
    skipAuthHandling: true,
    successSchema: AuthUserSchema,
  });
}

// Register no longer returns the user object; both new-email and duplicate-email
// branches return `{ status: "received" }`. Discover the real outcome by
// refetching `/me` after this resolves.
export function register(request: RegisterRequest) {
  return apiRequest('/api/auth/register', {
    body: request,
    method: 'POST',
    skipAuthHandling: true,
    successSchema: RegisterAcknowledgementSchema,
  });
}

export function logout() {
  return apiRequest('/api/auth/logout', {
    method: 'POST',
    skipAuthHandling: true,
    successSchema: z.null(),
  });
}
