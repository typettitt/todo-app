import { useSyncExternalStore } from 'react';

// Single source of truth for the in-app theme. The FOUC bootstrap module in
// fouc.ts mirrors this storage key + class-name contract — keep them in
// sync if either ever changes.
export type ThemeMode = 'light' | 'dark';

const STORAGE_KEY = 'todoapp.theme';
const DARK_CLASS = 'dark';

type Listener = (mode: ThemeMode) => void;

const listeners = new Set<Listener>();

function readStored(): ThemeMode | null {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    return raw === 'light' || raw === 'dark' ? raw : null;
  } catch {
    return null;
  }
}

function prefersDark(): boolean {
  if (typeof window === 'undefined' || !window.matchMedia) {
    return false;
  }

  return window.matchMedia('(prefers-color-scheme: dark)').matches;
}

function applyClass(mode: ThemeMode): void {
  if (typeof document === 'undefined') {
    return;
  }

  const root = document.documentElement;
  if (mode === 'dark') {
    root.classList.add(DARK_CLASS);
  } else {
    root.classList.remove(DARK_CLASS);
  }
}

function emit(mode: ThemeMode): void {
  for (const listener of listeners) {
    listener(mode);
  }
}

export function getInitialTheme(): ThemeMode {
  const stored = readStored();
  if (stored) {
    return stored;
  }

  if (typeof window !== 'undefined') {
    return prefersDark() ? 'dark' : 'light';
  }

  return 'dark';
}

export function setTheme(mode: ThemeMode): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, mode);
  } catch {
    // Storage may be unavailable (private mode, quota). The class still
    // toggles for the current page session; we just lose persistence.
  }

  applyClass(mode);
  emit(mode);
}

export function toggleTheme(): ThemeMode {
  const current = getCurrentMode();
  const next: ThemeMode = current === 'dark' ? 'light' : 'dark';
  setTheme(next);
  return next;
}

export function subscribe(listener: Listener): () => void {
  listeners.add(listener);

  let mediaCleanup: (() => void) | null = null;
  if (typeof window !== 'undefined' && window.matchMedia) {
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const handleMediaChange = (event: MediaQueryListEvent) => {
      // System preference only drives the theme when there's no user override.
      if (readStored() !== null) {
        return;
      }

      const next: ThemeMode = event.matches ? 'dark' : 'light';
      applyClass(next);
      listener(next);
    };

    media.addEventListener('change', handleMediaChange);
    mediaCleanup = () => media.removeEventListener('change', handleMediaChange);
  }

  return () => {
    listeners.delete(listener);
    mediaCleanup?.();
  };
}

function getCurrentMode(): ThemeMode {
  if (typeof document !== 'undefined') {
    return document.documentElement.classList.contains(DARK_CLASS) ? 'dark' : 'light';
  }

  return getInitialTheme();
}

function subscribeForHook(notify: () => void): () => void {
  const wrapped: Listener = () => notify();
  listeners.add(wrapped);

  let mediaCleanup: (() => void) | null = null;
  if (typeof window !== 'undefined' && window.matchMedia) {
    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const handleMediaChange = () => {
      if (readStored() !== null) {
        return;
      }
      notify();
    };
    media.addEventListener('change', handleMediaChange);
    mediaCleanup = () => media.removeEventListener('change', handleMediaChange);
  }

  return () => {
    listeners.delete(wrapped);
    mediaCleanup?.();
  };
}

export function useTheme(): readonly [ThemeMode, (mode: ThemeMode) => void] {
  const mode = useSyncExternalStore(subscribeForHook, getCurrentMode, getInitialTheme);
  return [mode, setTheme] as const;
}
