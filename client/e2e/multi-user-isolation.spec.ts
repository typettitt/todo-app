import { expect, test, type APIRequestContext, type BrowserContext } from '@playwright/test';
import {
  assertAuthCookieAttributes,
  baseURLFromTestInfo,
  createApiContext,
  createTamperedApiContext,
  createTodo,
  expectExactIdSet,
  expectNoIdOverlap,
  expectStatus,
  listTodos,
  sendTodoItemRequest,
  setupSeededUsers,
} from './helpers/multi-user';

type ItemVerb = 'GET' | 'PUT' | 'PATCH' | 'DELETE';

const ITEM_VERBS: ItemVerb[] = ['GET', 'PUT', 'PATCH', 'DELETE'];
const RACE_CREATE_COUNT = 10;

test.describe('@security multi-user isolation', () => {
  test.describe.configure({ mode: 'parallel' });

  test('cross-tenant item endpoints and auth cookies enforce isolation @security', async ({
    playwright,
    request,
  }, testInfo) => {
    const baseURL = baseURLFromTestInfo(testInfo);
    const seeded = await setupSeededUsers(request, playwright.request, baseURL);
    let anonymousApi: APIRequestContext | undefined;
    let tamperedApi: APIRequestContext | undefined;

    try {
      expect(seeded.alice.authCookie.value).not.toBe(seeded.bob.authCookie.value);
      assertAuthCookieAttributes(seeded.alice, baseURL);
      assertAuthCookieAttributes(seeded.bob, baseURL);

      anonymousApi = await createApiContext(playwright.request, baseURL);
      tamperedApi = await createTamperedApiContext(playwright.request, baseURL, seeded.alice);

      const bobTarget = seeded.bob.todos[0];
      for (const verb of ITEM_VERBS) {
        const aliceResponse = await sendTodoItemRequest(seeded.aliceApi, verb, bobTarget);
        await expectStatus(
          aliceResponse,
          404,
          `${verb} ${routeForVerb(verb)} as Alice against Bob todo ${bobTarget.id}`,
        );

        const anonymousResponse = await sendTodoItemRequest(anonymousApi, verb, bobTarget);
        await expectStatus(
          anonymousResponse,
          401,
          `${verb} ${routeForVerb(verb)} as anonymous against todo ${bobTarget.id}`,
        );

        const tamperedResponse = await sendTodoItemRequest(tamperedApi, verb, bobTarget);
        await expectStatus(
          tamperedResponse,
          401,
          `${verb} ${routeForVerb(verb)} with tampered JWT against todo ${bobTarget.id}`,
        );
      }

      const bobStillOwnsTodo = await sendTodoItemRequest(seeded.bobApi, 'GET', bobTarget);
      await expectStatus(
        bobStillOwnsTodo,
        200,
        `GET /api/todos/{id} as Bob after Alice cross-tenant attempts`,
      );
    } finally {
      const disposals = [seeded.dispose()];
      if (anonymousApi) {
        disposals.push(anonymousApi.dispose());
      }
      if (tamperedApi) {
        disposals.push(tamperedApi.dispose());
      }
      await Promise.all(disposals);
    }
  });

  test('list endpoint returns only caller-owned todos by id @security', async ({
    playwright,
    request,
  }, testInfo) => {
    const baseURL = baseURLFromTestInfo(testInfo);
    const seeded = await setupSeededUsers(request, playwright.request, baseURL);

    try {
      const [aliceList, bobList] = await Promise.all([
        listTodos(seeded.aliceApi),
        listTodos(seeded.bobApi),
      ]);
      const aliceIds = aliceList.items.map((todo) => todo.id);
      const bobIds = bobList.items.map((todo) => todo.id);
      const expectedAliceIds = seeded.alice.todos.map((todo) => todo.id);
      const expectedBobIds = seeded.bob.todos.map((todo) => todo.id);

      expect(aliceList.total).toBe(expectedAliceIds.length);
      expect(bobList.total).toBe(expectedBobIds.length);
      expectExactIdSet(aliceIds, expectedAliceIds);
      expectExactIdSet(bobIds, expectedBobIds);
      expectNoIdOverlap(aliceIds, expectedBobIds);
      expectNoIdOverlap(bobIds, expectedAliceIds);
    } finally {
      await seeded.dispose();
    }
  });

  test('UI browser contexts do not show Alice-created todos in Bob list @security', async ({
    browser,
    playwright,
    request,
  }, testInfo) => {
    const baseURL = baseURLFromTestInfo(testInfo);
    const seeded = await setupSeededUsers(request, playwright.request, baseURL);
    let aliceContext: BrowserContext | undefined;
    let bobContext: BrowserContext | undefined;

    try {
      aliceContext = await browser.newContext({
        baseURL,
        storageState: seeded.alice.storageState,
      });
      bobContext = await browser.newContext({
        baseURL,
        storageState: seeded.bob.storageState,
      });

      const alicePage = await aliceContext.newPage();
      const bobPage = await bobContext.newPage();
      const sentinel = `alice-ui-sentinel-${seeded.nonce}`;

      await Promise.all([alicePage.goto('/todos'), bobPage.goto('/todos')]);
      await expect(
        alicePage.getByRole('heading', { name: seeded.alice.todos[0].title }),
      ).toBeVisible();
      await expect(bobPage.getByRole('heading', { name: seeded.bob.todos[0].title })).toBeVisible();

      await alicePage.getByRole('button', { name: 'New Todo' }).click();
      const createDialog = alicePage.getByTestId('dialog-new-todo');
      await expect(createDialog).toBeVisible();
      await createDialog.getByRole('textbox', { name: 'Title' }).fill(sentinel);
      await createDialog.getByRole('textbox', { name: 'Description' }).fill('Alice-only UI todo');
      await createDialog.getByRole('combobox', { name: 'Priority' }).selectOption('High');
      await createDialog.getByTestId('todo-create-submit').click();
      await expect(createDialog).toBeHidden();
      await expect(alicePage.getByRole('heading', { name: sentinel })).toBeVisible();

      await bobPage.reload({ waitUntil: 'domcontentloaded' });
      await expect(bobPage.getByRole('heading', { name: seeded.bob.todos[0].title })).toBeVisible();
      await expect(bobPage.getByRole('heading', { name: sentinel })).toHaveCount(0);
      await expect(bobPage.getByText(sentinel)).toHaveCount(0);
    } finally {
      const disposals = [seeded.dispose()];
      if (aliceContext) {
        disposals.push(aliceContext.close());
      }
      if (bobContext) {
        disposals.push(bobContext.close());
      }
      await Promise.all(disposals);
    }
  });

  test('concurrent creates keep final counts and ownership exact @security', async ({
    playwright,
    request,
  }, testInfo) => {
    const baseURL = baseURLFromTestInfo(testInfo);
    const seeded = await setupSeededUsers(request, playwright.request, baseURL);

    try {
      const aliceCreatePromises = Array.from({ length: RACE_CREATE_COUNT }, (_value, index) =>
        createTodo(seeded.aliceApi, `alice-race-${seeded.nonce}-${index + 1}`, {
          description: 'Alice concurrent create',
          tags: ['alice', 'race'],
        }),
      );
      const bobCreatePromises = Array.from({ length: RACE_CREATE_COUNT }, (_value, index) =>
        createTodo(seeded.bobApi, `bob-race-${seeded.nonce}-${index + 1}`, {
          description: 'Bob concurrent create',
          tags: ['bob', 'race'],
        }),
      );

      const [aliceCreated, bobCreated] = await Promise.all([
        Promise.all(aliceCreatePromises),
        Promise.all(bobCreatePromises),
      ]);

      const [aliceList, bobList] = await Promise.all([
        listTodos(seeded.aliceApi),
        listTodos(seeded.bobApi),
      ]);
      const expectedAliceIds = [...seeded.alice.todos, ...aliceCreated].map((todo) => todo.id);
      const expectedBobIds = [...seeded.bob.todos, ...bobCreated].map((todo) => todo.id);
      const aliceIds = aliceList.items.map((todo) => todo.id);
      const bobIds = bobList.items.map((todo) => todo.id);

      expect(aliceList.total).toBe(3 + RACE_CREATE_COUNT);
      expect(bobList.total).toBe(3 + RACE_CREATE_COUNT);
      expectExactIdSet(aliceIds, expectedAliceIds);
      expectExactIdSet(bobIds, expectedBobIds);
      expectNoIdOverlap(aliceIds, expectedBobIds);
      expectNoIdOverlap(bobIds, expectedAliceIds);
    } finally {
      await seeded.dispose();
    }
  });
});

function routeForVerb(verb: ItemVerb): string {
  return verb === 'PATCH' ? '/api/todos/{id}/complete' : '/api/todos/{id}';
}
