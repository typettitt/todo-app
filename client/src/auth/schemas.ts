import { z } from 'zod';
import type { components } from '../lib/openapi-types';

export type LoginRequest = components['schemas']['LoginRequest'];
export type RegisterRequest = components['schemas']['RegisterRequest'];

export type AuthUser = {
  id: string;
  email: string;
  role: 'Basic' | 'Admin';
};

export const AuthUserSchema = z.object({
  id: z.string().uuid(),
  email: z.string().email(),
  role: z.enum(['Basic', 'Admin']),
}) satisfies z.ZodType<AuthUser>;

export const LoginRequestSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, 'Email is required.')
    .max(256, 'Email must be 256 characters or fewer.'),
  password: z.string().min(1, 'Password is required.'),
}) satisfies z.ZodType<LoginRequest>;

export const RegisterRequestSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, 'Email is required.')
    .email('Email must be a valid email address.')
    .max(256, 'Email must be 256 characters or fewer.'),
  password: z
    .string()
    .min(1, 'Password is required.')
    .min(8, 'Password must be at least 8 characters.')
    .max(256, 'Password must be 256 characters or fewer.'),
}) satisfies z.ZodType<RegisterRequest>;

/**
 * Register-enumeration-oracle elimination.
 *
 * The server returns an identical 200 body for both branches of register
 * (new email AND duplicate email). The body is just `{ status: "received" }`
 * — no user object, no role, nothing that distinguishes "new" from
 * "already exists". We discover whether registration actually succeeded by
 * triggering a `/me` refetch right after; if /me returns a user, we created
 * an account; if /me 401s, the email was already registered (or some other
 * server-side reason rejected us silently). See `docs/decisions.md`.
 */
export const RegisterAcknowledgementSchema = z.object({
  status: z.literal('received'),
});

export type RegisterAcknowledgement = z.infer<typeof RegisterAcknowledgementSchema>;
