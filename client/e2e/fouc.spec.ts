import { expect, test } from '@playwright/test';
import sharp from 'sharp';

// FOUC = "flash of unstyled content". The bootstrap imported by main.tsx flips
// `<html class="dark">` before React renders. This spec proves the flip happens
// early enough that no light pixels leak in dark mode (and vice versa) by
// sampling mean luminance at 16/48/96/160ms post-reload.
//
// The auth page has enough real layout to luminance-compare without spending
// auth rate-limit budget on test setup.

const SAMPLE_DELAYS_MS = [16, 48, 96, 160];

type ThemeMode = 'light' | 'dark';

test.describe('FOUC @smoke', () => {
  for (const mode of ['light', 'dark'] as const) {
    test(`no flash on reload (${mode})`, async ({ page }) => {
      await page.goto('/login');
      await expect(page.getByRole('heading', { name: 'Todo App' })).toBeVisible();

      // Pin the chosen theme + reload. The FOUC bootstrap reads localStorage
      // on the very next document, so the first sampled frames should already
      // be the right theme.
      await page.evaluate((m: ThemeMode) => {
        localStorage.setItem('todoapp.theme', m);
      }, mode);

      const reloadPromise = page.reload({ waitUntil: 'domcontentloaded' });

      // Capture screenshots in parallel at increasing delays. We start the
      // timers from now so they line up with the reload starting point. The
      // first frame is at +16ms, before React typically commits.
      const captures = await Promise.all(
        SAMPLE_DELAYS_MS.map((delay) =>
          waitMs(delay).then(() => page.screenshot({ fullPage: false })),
        ),
      );
      await reloadPromise;

      const luminances = await Promise.all(captures.map(meanLuminance));

      // All four samples must cluster within 0.10 of each other. If the FOUC
      // script regressed, the first frame would be the OS-default light bg
      // while the second+ would be the requested mode — a delta well above
      // 0.10 in either direction.
      const min = Math.min(...luminances);
      const max = Math.max(...luminances);
      expect(
        max - min,
        `luminance samples ${luminances.map((l) => l.toFixed(3)).join(', ')}`,
      ).toBeLessThan(0.1);
    });
  }
});

function waitMs(ms: number): Promise<void> {
  return new Promise((resolve) => {
    setTimeout(resolve, ms);
  });
}

async function meanLuminance(png: Buffer): Promise<number> {
  // Downsample to a 32×32 RGB grid and average the relative luminance per
  // ITU-R BT.709. That's plenty for FOUC detection (we only care about
  // bg light↔dark flips, not pixel-perfect comparisons).
  const { data, info } = await sharp(png)
    .resize(32, 32, { fit: 'cover' })
    .removeAlpha()
    .raw()
    .toBuffer({ resolveWithObject: true });

  const channels = info.channels;
  const pixelCount = data.length / channels;
  let total = 0;

  for (let i = 0; i < data.length; i += channels) {
    const r = data[i] / 255;
    const g = data[i + 1] / 255;
    const b = data[i + 2] / 255;
    total += 0.2126 * r + 0.7152 * g + 0.0722 * b;
  }

  return total / pixelCount;
}
