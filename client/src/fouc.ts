// FOUC = "flash of unstyled content". This module flips
// `<html class="dark">` BEFORE React hydrates so the very first painted frame
// is already in the user-selected (or system-preferred) theme. Imported as a
// side-effect-only ESM import at the very top of `main.tsx` — must run before
// `createRoot(...).render(...)`.
//
// Lives here (not inline in `index.html`) so the SPA's CSP can be a strict
// `script-src 'self'` without a hash exception or `'unsafe-inline'`.
// Storage key + class-name contract mirrors `lib/theme.ts` — keep them in
// sync if either ever changes.

const STORAGE_KEY = 'todoapp.theme';
const DARK_CLASS = 'dark';

function applyInitialTheme(): void {
  if (typeof document === 'undefined' || typeof window === 'undefined') {
    return;
  }

  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    const prefersDark =
      typeof window.matchMedia === 'function' &&
      window.matchMedia('(prefers-color-scheme: dark)').matches;
    const shouldUseDark = stored === 'dark' || (stored !== 'light' && prefersDark);

    document.documentElement.classList.toggle(DARK_CLASS, shouldUseDark);
  } catch {
    // Storage can be unavailable; leave the server/default class untouched.
  }
}

applyInitialTheme();
