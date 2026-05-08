/**
 * Screenshot harness.
 *
 * Captures 15 named scenes at desktop (1280x800) + mobile (390x844), in both
 * light and dark themes, for visual review. Output: docs/screenshots/<device>/
 * <scene>-<theme>.png (60 PNGs total).
 *
 * Usage: BASE_URL=http://localhost:8080 npm run screenshots
 *
 * Determinism notes:
 * - Disables CSS animations + transitions and hides the caret via injected
 *   stylesheet so re-runs diff cleanly.
 * - Waits for `networkidle` after each navigation.
 * - Logs in as the demo seed user (creds shown in the README quick-start)
 *   so populated scenes have the deterministic >=500-todo seed dataset.
 * - Uses ONE shared `BrowserContext` per device so the auth cookie persists
 *   across all scenes and both themes - modal scenes 11/12/13 used to be
 *   flaky because each scene got a fresh context with no cookie.
 */
import { chromium, type BrowserContext, type Page } from '@playwright/test';
import { mkdir } from 'node:fs/promises';
import { join } from 'node:path';

type ThemeMode = 'light' | 'dark';

interface DeviceProfile {
  name: 'desktop' | 'mobile';
  viewport: { width: number; height: number };
}

interface SceneContext {
  baseUrl: string;
  email: string;
  credential: string;
}

interface Scene {
  id: string;
  /**
   * Returns once the scene is on screen and ready to capture. The returned
   * page may be the shared page or a freshly-navigated one - the caller
   * always screenshots whatever is returned.
   */
  setup: (page: Page, ctx: SceneContext) => Promise<Page>;
  /** Auth required before this scene can be set up. */
  requiresAuth: boolean;
}

const DEVICES: DeviceProfile[] = [
  { name: 'desktop', viewport: { width: 1280, height: 800 } },
  { name: 'mobile', viewport: { width: 390, height: 844 } },
];

const THEMES: ThemeMode[] = ['light', 'dark'];

const DETERMINISM_CSS = `
  *, *::before, *::after {
    animation: none !important;
    transition: none !important;
    caret-color: transparent !important;
  }
`;

const SCENES: Scene[] = [
  {
    id: '01-login',
    requiresAuth: false,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/login`, { waitUntil: 'networkidle' });
      return page;
    },
  },
  {
    id: '02-register',
    requiresAuth: false,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/register`, { waitUntil: 'networkidle' });
      return page;
    },
  },
  {
    id: '03-login-error',
    requiresAuth: false,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/login`, { waitUntil: 'networkidle' });
      await page.getByLabel('Email').fill('does-not-exist@example.com');
      await page.getByRole('textbox', { name: 'Password' }).fill('WrongPassword!1');
      await page.getByRole('button', { name: /sign in/i }).click();
      await page.waitForLoadState('networkidle');
      return page;
    },
  },
  {
    id: '04-todos-empty',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos`, { waitUntil: 'networkidle' });
      return page;
    },
  },
  {
    id: '05-todos-populated',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos`, { waitUntil: 'networkidle' });
      // Ensure at least one row is rendered so the populated scene is real.
      await page.getByRole('listitem').first().waitFor({ state: 'visible' });
      return page;
    },
  },
  {
    id: '06-todos-active',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos?status=Active`, {
        waitUntil: 'networkidle',
      });
      return page;
    },
  },
  {
    id: '07-todos-completed',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos?status=Completed`, {
        waitUntil: 'networkidle',
      });
      return page;
    },
  },
  {
    id: '08-todos-due-today',
    requiresAuth: true,
    setup: async (page, ctx) => {
      const today = new Date().toISOString().slice(0, 10);
      await page.goto(`${ctx.baseUrl}/todos?status=DueToday&today=${today}`, {
        waitUntil: 'networkidle',
      });
      return page;
    },
  },
  {
    id: '09-todos-search',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos?search=screenshot`, {
        waitUntil: 'networkidle',
      });
      return page;
    },
  },
  {
    id: '10-todos-calendar-week',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos?view=calendar`, {
        waitUntil: 'networkidle',
      });
      await page.getByRole('region', { name: 'Todo calendar' }).waitFor({
        state: 'visible',
      });
      return page;
    },
  },
  {
    id: '11-modal-new',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos`, { waitUntil: 'networkidle' });
      await page.getByRole('button', { name: 'New Todo' }).click();
      await page.getByTestId('dialog-new-todo').waitFor({ state: 'visible' });
      return page;
    },
  },
  {
    id: '12-modal-edit',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos`, { waitUntil: 'networkidle' });
      // Ensure the seeded edit row is visible before clicking; the seeder
      // guarantees an "Edit screenshot todo" exists.
      const row = page.getByRole('listitem').first();
      await row.waitFor({ state: 'visible' });
      await row.click();
      await row.getByRole('button', { name: /edit todo/i }).click();
      await page.getByTestId('dialog-edit-todo').waitFor({ state: 'visible' });
      return page;
    },
  },
  {
    id: '13-modal-delete',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos`, { waitUntil: 'networkidle' });
      const row = page.getByRole('listitem').first();
      await row.waitFor({ state: 'visible' });
      await row.click();
      await row.getByRole('button', { name: /delete todo/i }).click();
      await page.getByTestId('dialog-delete-todo').waitFor({ state: 'visible' });
      return page;
    },
  },
  {
    id: '14-todos-mobile-overflow',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos`, { waitUntil: 'networkidle' });
      return page;
    },
  },
  {
    id: '15-todos-calendar-month',
    requiresAuth: true,
    setup: async (page, ctx) => {
      await page.goto(`${ctx.baseUrl}/todos?view=calendar`, {
        waitUntil: 'networkidle',
      });
      await page.getByRole('tab', { name: 'Month' }).click();
      await page.getByRole('region', { name: 'Todo calendar' }).waitFor({
        state: 'visible',
      });
      return page;
    },
  },
];

async function main(): Promise<void> {
  const baseUrl = process.env.BASE_URL ?? 'http://localhost:5173';
  const outRoot = join(process.cwd(), '..', 'docs', 'screenshots');
  // Demo seed user - the compose stack populates this account with >=500
  // todos via the demo seeder, which gives deterministic content for the
  // populated, calendar, and modal scenes. Defaults match the README
  // quick-start; override via SCREENSHOTS_EMAIL / SCREENSHOTS_CRED env vars.
  const ctx: SceneContext = {
    baseUrl,
    email: process.env.SCREENSHOTS_EMAIL ?? 'demo@example.com',
    credential: process.env.SCREENSHOTS_CRED ?? '',
  };
  if (!ctx.credential) {
    throw new Error(
      'SCREENSHOTS_CRED env var is required. Use the demo seed creds shown in the README quick-start.',
    );
  }

  console.log(`Screenshot harness starting against ${baseUrl}`);

  const browser = await chromium.launch();
  try {
    for (const device of DEVICES) {
      const deviceDir = join(outRoot, device.name);
      await mkdir(deviceDir, { recursive: true });

      // ONE context per device - auth cookie persists across all scenes
      // and both themes for that device.
      const context = await browser.newContext({
        // The production CSP intentionally rejects inline styles. The screenshot
        // harness injects deterministic CSS to freeze animations/carets, so only
        // this Playwright context bypasses CSP.
        bypassCSP: true,
        viewport: device.viewport,
      });
      try {
        // Auth once via the API so cookies are set on the shared context.
        const bootstrapPage = await context.newPage();
        await loginViaApi(bootstrapPage, ctx);
        // Make sure auth has propagated before any scene runs.
        await bootstrapPage.goto(`${ctx.baseUrl}/todos`, {
          waitUntil: 'networkidle',
        });
        await bootstrapPage.close();

        for (const theme of THEMES) {
          for (const scene of SCENES) {
            const outPath = join(deviceDir, `${scene.id}-${theme}.png`);
            await captureScene({
              context,
              ctx: ctx,
              outPath,
              scene,
              theme,
            });
            console.log(`  wrote ${outPath}`);
          }
        }
      } finally {
        await context.close();
      }
    }
  } finally {
    await browser.close();
  }
}

interface CaptureArgs {
  context: BrowserContext;
  ctx: SceneContext;
  outPath: string;
  scene: Scene;
  theme: ThemeMode;
}

async function captureScene(args: CaptureArgs): Promise<void> {
  const { context, ctx, outPath, scene, theme } = args;

  // A fresh page per scene keeps the URL/state clean while reusing the
  // shared cookie jar. The init script primes localStorage with the theme
  // BEFORE any navigation so the FOUC bootstrap picks the right class on
  // first paint.
  const page = await context.newPage();
  await page.addInitScript((mode: ThemeMode) => {
    try {
      localStorage.setItem('todoapp.theme', mode);
    } catch {
      // ignore
    }
  }, theme);

  try {
    const ready = await scene.setup(page, ctx);
    await ready.addStyleTag({ content: DETERMINISM_CSS });
    await ready.waitForLoadState('networkidle').catch(() => {});
    await ready.screenshot({ fullPage: true, path: outPath });
  } finally {
    await page.close();
  }
}

async function loginViaApi(page: Page, ctx: SceneContext): Promise<void> {
  // Log in as the demo seed user. The demo account is provisioned by the
  // server's demo seeder during compose start-up and is populated with the
  // deterministic >=500-todo dataset that the populated, calendar, and
  // modal scenes rely on.
  const loginResp = await page.request.post(`${ctx.baseUrl}/api/auth/login`, {
    data: { email: ctx.email, password: ctx.credential },
  });
  if (!loginResp.ok()) {
    throw new Error(
      `Demo login failed (status ${loginResp.status()}). ` +
        `Confirm the compose stack is up and the demo seeder ran ` +
        `(check /api/health/ready and 'Testing:DisableDemoSeed').`,
    );
  }
}

main().catch((error: unknown) => {
  console.error(error);
  process.exit(1);
});
