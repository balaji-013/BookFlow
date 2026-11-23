import { test, expect } from '@playwright/test';

// Critical path: register a bookstore → add a book with opening stock → bill it at the POS → invoice shows stock deducted.
test('register, add book, complete a cash sale', async ({ page }) => {
  const stamp = Date.now();
  await page.goto('/register');
  await page.getByLabel('Bookstore name').fill(`E2E Books ${stamp}`);
  await page.getByLabel('Owner name').fill('E2E Owner');
  await page.getByLabel('Email').fill(`owner${stamp}@e2e.local`);
  await page.getByLabel('Password (min 8 characters)').fill('Password123!');
  await page.getByRole('button', { name: 'Create account' }).click();
  await expect(page).toHaveURL(/\/books/);

  await page.getByRole('button', { name: 'Add book' }).click();
  await page.getByLabel('Title').fill('Ponniyin Selvan 1');
  await page.getByLabel('Selling price').fill('250');
  await page.getByLabel('Opening stock').fill('5');
  await page.getByRole('button', { name: 'Save' }).click();
  await expect(page.getByText('Book saved')).toBeVisible();
  await expect(page.getByRole('cell', { name: /Ponniyin Selvan 1/ })).toBeVisible();

  await page.goto('/pos');
  const search = page.getByLabel('Scan or search books');
  await search.fill('Ponniyin');
  await page.getByRole('option', { name: /Ponniyin Selvan 1/ }).first().click();
  await expect(page.getByText('Complete sale')).toBeVisible();
  await page.getByRole('button', { name: 'Exact' }).click();
  await page.getByRole('button', { name: /Complete sale/ }).click();
  await expect(page.getByText(/Invoice INV\//)).toBeVisible();
  await expect(page).toHaveURL(/\/invoices\//);
  await expect(page.getByText('Paid', { exact: true })).toBeVisible();

  await page.goto('/books');
  await expect(page.getByRole('row', { name: /Ponniyin Selvan 1/ }).getByText('4', { exact: true })).toBeVisible();
});

test('oversell is rejected at the POS', async ({ page }) => {
  const stamp = Date.now();
  await page.goto('/register');
  await page.getByLabel('Bookstore name').fill(`E2E Oversell ${stamp}`);
  await page.getByLabel('Owner name').fill('E2E Owner');
  await page.getByLabel('Email').fill(`over${stamp}@e2e.local`);
  await page.getByLabel('Password (min 8 characters)').fill('Password123!');
  await page.getByRole('button', { name: 'Create account' }).click();
  await page.getByRole('button', { name: 'Add book' }).click();
  await page.getByLabel('Title').fill('Single copy');
  await page.getByLabel('Selling price').fill('100');
  await page.getByLabel('Opening stock').fill('1');
  await page.getByRole('button', { name: 'Save' }).click();
  await page.goto('/pos');
  await page.getByLabel('Scan or search books').fill('Single');
  await page.getByRole('option', { name: /Single copy/ }).first().click();
  await page.getByRole('button', { name: 'Increase' }).click();
  await page.getByRole('button', { name: 'Exact' }).click();
  await page.getByRole('button', { name: /Complete sale/ }).click();
  await expect(page.getByText(/Not enough stock/)).toBeVisible();
});
