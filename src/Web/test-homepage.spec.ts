import { test } from '@playwright/test';

test('homepage screenshot', async ({ page }) => {
  await page.goto('http://localhost:5173');
  await page.screenshot({ path: 'homepage.png', fullPage: true });
});
