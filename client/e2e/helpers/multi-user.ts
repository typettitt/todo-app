import { expect, type APIRequestContext, type APIResponse, type TestInfo } from '@playwright/test';
import { randomUUID } from 'node:crypto';

export type StorageState = Awaited<ReturnType<APIRequestContext['storageState']>>;
export type StorageCookie = StorageState['cookies'][number];
export type Priority = 'Low' | 'Medium' | 'High';

export type Todo = {
  id: string;
  title: string;
  description: string | null;
  dueDate: string | null;
  priority: Priority;
  isCompleted: boolean;
  completedAt: string | null;
  tags: string[];
  rowVersion: number;
  createdAt: string;
  updatedAt: string;
};

export type PagedTodos = {
  items: Todo[];
  page: number;
  pageSize: number;
  total: number;
  hasNext: boolean;
};

export type RegisteredUser = {
  authCookie: StorageCookie;
  email: string;
  label: 'alice' | 'bob';
  password: string;
  setCookieHeader: string;
  storageState: StorageState;
};

export type SeededUser = RegisteredUser & {
  todos: Todo[];
};

export type SeededUsers = {
  alice: SeededUser;
  aliceApi: APIRequestContext;
  bob: SeededUser;
  bobApi: APIRequestContext;
  dispose: () => Promise<void>;
  nonce: string;
};

type ApiRequestFactory = {
  newContext: (options?: {
    baseURL?: string;
    storageState?: StorageState;
  }) => Promise<APIRequestContext>;
};

type CreateTodoPayload = {
  description: string | null;
  dueDate: string | null;
  priority: Priority;
  tags: string[];
  title: string;
};

type UpdateTodoPayload = CreateTodoPayload & {
  rowVersion: number;
};

type CompleteTodoPayload = {
  isCompleted: boolean;
  rowVersion: number;
};

const PASSWORD = 'E2eMulti!123';
const AUTH_COOKIE = 'auth';
const TODO_PAGE_QUERY = '?page=1&pageSize=100&sortBy=CreatedAt&sortDir=Asc';

export function baseURLFromTestInfo(testInfo: TestInfo): string {
  const baseURL = testInfo.project.use.baseURL;
  if (typeof baseURL !== 'string' || baseURL.length === 0) {
    throw new Error('Playwright baseURL must be configured for multi-user isolation tests.');
  }

  return baseURL;
}

export async function setupSeededUsers(
  request: APIRequestContext,
  requestFactory: ApiRequestFactory,
  baseURL: string,
): Promise<SeededUsers> {
  const nonce = randomUUID();
  const alice = await registerUser(request, 'alice', nonce);
  const bob = await registerUser(request, 'bob', nonce);
  const aliceApi = await createApiContext(requestFactory, baseURL, alice.storageState);
  const bobApi = await createApiContext(requestFactory, baseURL, bob.storageState);

  try {
    const [aliceTodos, bobTodos] = await Promise.all([
      seedTodos(aliceApi, alice.label, nonce),
      seedTodos(bobApi, bob.label, nonce),
    ]);

    return {
      alice: { ...alice, todos: aliceTodos },
      aliceApi,
      bob: { ...bob, todos: bobTodos },
      bobApi,
      dispose: async () => {
        await Promise.all([aliceApi.dispose(), bobApi.dispose()]);
      },
      nonce,
    };
  } catch (error) {
    await Promise.all([aliceApi.dispose(), bobApi.dispose()]);
    throw error;
  }
}

export async function createApiContext(
  requestFactory: ApiRequestFactory,
  baseURL: string,
  storageState?: StorageState,
): Promise<APIRequestContext> {
  return requestFactory.newContext({ baseURL, storageState });
}

export async function createTamperedApiContext(
  requestFactory: ApiRequestFactory,
  baseURL: string,
  user: RegisteredUser,
): Promise<APIRequestContext> {
  return createApiContext(requestFactory, baseURL, tamperStorageState(user.storageState));
}

export async function createTodo(
  api: APIRequestContext,
  title: string,
  overrides: Partial<Omit<CreateTodoPayload, 'title'>> = {},
): Promise<Todo> {
  const payload: CreateTodoPayload = {
    description: null,
    dueDate: null,
    priority: 'Low',
    tags: [],
    title,
    ...overrides,
  };

  const response = await api.post('/api/todos', { data: payload });
  await expectStatus(response, 201, `POST /api/todos (${title})`);
  const json: unknown = await response.json();
  return parseTodo(json, `POST /api/todos response (${title})`);
}

export async function listTodos(api: APIRequestContext): Promise<PagedTodos> {
  const response = await api.get(`/api/todos${TODO_PAGE_QUERY}`);
  await expectStatus(response, 200, 'GET /api/todos');
  const json: unknown = await response.json();
  return parsePagedTodos(json);
}

export async function sendTodoItemRequest(
  api: APIRequestContext,
  verb: 'GET' | 'PUT' | 'PATCH' | 'DELETE',
  target: Todo,
): Promise<APIResponse> {
  if (verb === 'GET') {
    return api.get(`/api/todos/${target.id}`);
  }

  if (verb === 'PUT') {
    return api.put(`/api/todos/${target.id}`, { data: updatePayload(target) });
  }

  if (verb === 'PATCH') {
    return api.patch(`/api/todos/${target.id}/complete`, { data: completePayload(target) });
  }

  return api.delete(`/api/todos/${target.id}`);
}

export async function expectStatus(
  response: APIResponse,
  expected: number,
  label: string,
): Promise<void> {
  if (response.status() === expected) {
    return;
  }

  const body = await response
    .text()
    .catch((error: unknown) =>
      error instanceof Error ? `<unreadable body: ${error.message}>` : '<unreadable body>',
    );
  expect(response.status(), `${label} returned body: ${body}`).toBe(expected);
}

export function expectExactIdSet(actualIds: Iterable<string>, expectedIds: Iterable<string>): void {
  expect([...actualIds].sort()).toEqual([...expectedIds].sort());
}

export function expectNoIdOverlap(leftIds: Iterable<string>, rightIds: Iterable<string>): void {
  const right = new Set(rightIds);
  const overlap = [...leftIds].filter((id) => right.has(id));
  expect(overlap).toEqual([]);
}

export function assertAuthCookieAttributes(user: RegisteredUser, baseURL: string): void {
  const parsed = parseSetCookie(user.setCookieHeader);
  const sameSite = parsed.attributeValues.get('samesite');
  const expectedSecure = expectsSecureCookie(baseURL);

  expect(user.authCookie.name).toBe(AUTH_COOKIE);
  expect(user.authCookie.path).toBe('/');
  expect(user.authCookie.httpOnly).toBe(true);
  expect(user.authCookie.secure).toBe(expectedSecure);
  expect(['Lax', 'Strict']).toContain(user.authCookie.sameSite);

  expect(parsed.name).toBe(AUTH_COOKIE);
  expect(parsed.attributes.has('httponly')).toBe(true);
  expect(parsed.attributes.has('secure')).toBe(expectedSecure);
  expect(sameSite).toBeDefined();
  expect(normalizeSameSite(requireStringValue(sameSite, 'SameSite'))).toBe(
    user.authCookie.sameSite,
  );

  if (!expectedSecure) {
    expect(user.authCookie.sameSite).toBe('Lax');
  }
}

function expectsSecureCookie(baseURL: string): boolean {
  return new URL(baseURL).protocol === 'https:';
}

async function registerUser(
  request: APIRequestContext,
  label: 'alice' | 'bob',
  nonce: string,
): Promise<RegisteredUser> {
  const email = `${label}-${nonce}@test.local`;
  const response = await request.post('/api/auth/register', {
    data: { email, password: PASSWORD },
  });
  // Register returns 200 + uniform body for both new-email and duplicate-email
  // branches. Cookie presence in the request context's storage state is the
  // network-observable signal that the new-email branch ran.
  await expectStatus(response, 200, `POST /api/auth/register (${label})`);

  const storageState = await request.storageState();
  const authCookie = findAuthCookie(storageState);
  const setCookieHeader = findSetCookieHeader(response, AUTH_COOKIE);

  return {
    authCookie,
    email,
    label,
    password: PASSWORD,
    setCookieHeader,
    storageState,
  };
}

async function seedTodos(
  api: APIRequestContext,
  label: 'alice' | 'bob',
  nonce: string,
): Promise<Todo[]> {
  return Promise.all(
    [1, 2, 3].map((index) =>
      createTodo(api, `${label}-${nonce}-seed-${index}`, {
        description: `${label} seed ${index}`,
        priority: index === 1 ? 'Low' : index === 2 ? 'Medium' : 'High',
        tags: [label, `seed-${index}`],
      }),
    ),
  );
}

function updatePayload(todo: Todo): UpdateTodoPayload {
  return {
    description: todo.description,
    dueDate: todo.dueDate,
    priority: todo.priority,
    rowVersion: todo.rowVersion,
    tags: todo.tags,
    title: `${todo.title} hijack attempt`,
  };
}

function completePayload(todo: Todo): CompleteTodoPayload {
  return {
    isCompleted: !todo.isCompleted,
    rowVersion: todo.rowVersion,
  };
}

function tamperStorageState(storageState: StorageState): StorageState {
  return {
    cookies: storageState.cookies.map((cookie) =>
      cookie.name === AUTH_COOKIE ? { ...cookie, value: tamperJwt(cookie.value) } : cookie,
    ),
    origins: storageState.origins.map((origin) => ({
      localStorage: origin.localStorage.map((entry) => ({ ...entry })),
      origin: origin.origin,
    })),
  };
}

function tamperJwt(value: string): string {
  for (let index = value.length - 1; index >= 0; index -= 1) {
    const current = value[index];
    if (current === '.') {
      continue;
    }

    const replacement = current === 'A' ? 'B' : 'A';
    return `${value.slice(0, index)}${replacement}${value.slice(index + 1)}`;
  }

  throw new Error('Cannot tamper empty auth cookie value.');
}

function findAuthCookie(storageState: StorageState): StorageCookie {
  const authCookie = storageState.cookies.find((cookie) => cookie.name === AUTH_COOKIE);
  if (!authCookie) {
    throw new Error(`Expected ${AUTH_COOKIE} cookie in storage state.`);
  }

  return authCookie;
}

function findSetCookieHeader(response: APIResponse, cookieName: string): string {
  const header = response
    .headersArray()
    .find(
      ({ name, value }) =>
        name.toLowerCase() === 'set-cookie' && value.startsWith(`${cookieName}=`),
    );

  if (!header) {
    throw new Error(`Expected Set-Cookie header for ${cookieName}.`);
  }

  return header.value;
}

function parseSetCookie(header: string): {
  attributes: Set<string>;
  attributeValues: Map<string, string>;
  name: string;
} {
  const [nameValue, ...attributes] = header.split(';').map((part) => part.trim());
  const [name] = nameValue.split('=');
  const attributeSet = new Set<string>();
  const attributeValues = new Map<string, string>();

  for (const attribute of attributes) {
    const [rawKey, ...rawValueParts] = attribute.split('=');
    const key = rawKey.toLowerCase();
    attributeSet.add(key);
    if (rawValueParts.length > 0) {
      attributeValues.set(key, rawValueParts.join('='));
    }
  }

  return { attributes: attributeSet, attributeValues, name };
}

function normalizeSameSite(value: string): StorageCookie['sameSite'] {
  const normalized = value.toLowerCase();
  if (normalized === 'lax') {
    return 'Lax';
  }

  if (normalized === 'strict') {
    return 'Strict';
  }

  if (normalized === 'none') {
    return 'None';
  }

  throw new Error(`Unexpected SameSite value '${value}'.`);
}

function parsePagedTodos(value: unknown): PagedTodos {
  const record = requireRecord(value, 'GET /api/todos response');
  const items = requireArray(record, 'items').map((item, index) =>
    parseTodo(item, `GET /api/todos item ${index}`),
  );

  return {
    hasNext: requireBoolean(record, 'hasNext'),
    items,
    page: requireNumber(record, 'page'),
    pageSize: requireNumber(record, 'pageSize'),
    total: requireNumber(record, 'total'),
  };
}

function parseTodo(value: unknown, label: string): Todo {
  const record = requireRecord(value, label);
  const priority = requireString(record, 'priority');
  if (!isPriority(priority)) {
    throw new Error(`${label}.priority must be Low, Medium, or High.`);
  }

  return {
    completedAt: requireNullableString(record, 'completedAt'),
    createdAt: requireString(record, 'createdAt'),
    description: requireNullableString(record, 'description'),
    dueDate: requireNullableString(record, 'dueDate'),
    id: requireString(record, 'id'),
    isCompleted: requireBoolean(record, 'isCompleted'),
    priority,
    rowVersion: requireNumber(record, 'rowVersion'),
    tags: requireArray(record, 'tags').map((tag, index) =>
      requireStringValue(tag, `tags[${index}]`),
    ),
    title: requireString(record, 'title'),
    updatedAt: requireString(record, 'updatedAt'),
  };
}

function isPriority(value: string): value is Priority {
  return value === 'Low' || value === 'Medium' || value === 'High';
}

function requireRecord(value: unknown, label: string): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    throw new Error(`${label} must be an object.`);
  }

  return value as Record<string, unknown>;
}

function requireString(record: Record<string, unknown>, key: string): string {
  return requireStringValue(record[key], key);
}

function requireStringValue(value: unknown, label: string): string {
  if (typeof value !== 'string') {
    throw new Error(`${label} must be a string.`);
  }

  return value;
}

function requireNullableString(record: Record<string, unknown>, key: string): string | null {
  const value = record[key];
  if (value === null) {
    return null;
  }

  return requireStringValue(value, key);
}

function requireNumber(record: Record<string, unknown>, key: string): number {
  const value = record[key];
  if (typeof value !== 'number') {
    throw new Error(`${key} must be a number.`);
  }

  return value;
}

function requireBoolean(record: Record<string, unknown>, key: string): boolean {
  const value = record[key];
  if (typeof value !== 'boolean') {
    throw new Error(`${key} must be a boolean.`);
  }

  return value;
}

function requireArray(record: Record<string, unknown>, key: string): unknown[] {
  const value = record[key];
  if (!Array.isArray(value)) {
    throw new Error(`${key} must be an array.`);
  }

  return value;
}
