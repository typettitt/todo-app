import '@testing-library/jest-dom/vitest';
import 'vitest-axe/extend-expect';
import * as matchers from 'vitest-axe/matchers';
import { afterAll, afterEach, beforeAll, expect, vi } from 'vitest';
import { resetApiClient } from '../lib/api';
import { queryClient } from '../lib/queryClient';
import { server } from './msw';

expect.extend(matchers);

Object.defineProperty(HTMLCanvasElement.prototype, 'getContext', {
  configurable: true,
  value: vi.fn(() => null),
});

const dialogProto = HTMLDialogElement.prototype as unknown as {
  close?: () => void;
  showModal?: () => void;
};
dialogProto.showModal = function showModal(this: HTMLDialogElement) {
  this.setAttribute('open', '');
  Object.defineProperty(this, 'open', { configurable: true, value: true });
};
dialogProto.close = function close(this: HTMLDialogElement) {
  if (!this.open) return;
  this.removeAttribute('open');
  Object.defineProperty(this, 'open', { configurable: true, value: false });
  this.dispatchEvent(new Event('close'));
};

// MSW lifecycle. The server starts with zero handlers; tests opt in to mocked
// endpoints via `server.use(...)`.
beforeAll(() => server.listen({ onUnhandledRequest: 'error' }));
afterEach(() => {
  server.resetHandlers();
  queryClient.clear();
  resetApiClient();
  vi.restoreAllMocks();
});
afterAll(() => server.close());
