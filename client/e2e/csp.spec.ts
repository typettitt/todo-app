import { expect, test } from '@playwright/test';

test.describe('transport headers @smoke', () => {
  test('SPA routes ship CSP/HSTS and emit no CSP violations', async ({ page, request }) => {
    const headerResponse = await request.get('/');
    expect(headerResponse.headers()['content-security-policy']).toBe(
      "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; object-src 'none'; frame-src 'none'; worker-src 'self'; manifest-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'",
    );
    expect(headerResponse.headers()['strict-transport-security']).toBe(
      'max-age=31536000; includeSubDomains',
    );

    const cspMessages: string[] = [];
    page.on('console', (message) => {
      const text = message.text();
      if (/content security policy|violat/i.test(text)) {
        cspMessages.push(text);
      }
    });

    await page.goto('/login');
    await expect(page.getByRole('heading', { name: 'Todo App' })).toBeVisible();

    await page.goto('/register');
    await expect(page.getByRole('heading', { name: /create account/i })).toBeVisible();

    const email = `csp-${Date.now()}@example.com`;
    await page.getByRole('textbox', { name: 'Email' }).fill(email);
    await page.getByRole('textbox', { name: 'Password' }).fill('CspTest!123');
    await page.getByRole('button', { name: 'Register' }).click();
    await expect(page).toHaveURL(/\/todos$/);
    await expect(page.getByRole('heading', { name: 'Todo Console' })).toBeVisible();

    await page.reload({ waitUntil: 'networkidle' });
    await expect(page.getByRole('heading', { name: 'Todo Console' })).toBeVisible();

    expect(cspMessages).toEqual([]);
  });
});
