import { expect, test, type Page, type Route } from '@playwright/test';

// Feature 187: the web UI passkey flow with the virtual authenticator of Chromium (CDP WebAuthn domain).
// The browser makes real passkeys. The service is a mock, because the real service checks the https public origin.
// Run with --trace off: recording every test costs CPU.

const b64 = (text: string) => Buffer.from(text).toString('base64url');

type MockState = { signedIn: boolean; registered: boolean; registrationBody: any; signInBody: any };

const installMock = async (page: Page, state: MockState) => {
  const json = (route: Route, body: unknown, status = 200) =>
    route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

  // Other API calls of the application get an empty list.
  await page.route('**/api/**', (route) => json(route, []));
  await page.route('**/api/auth/status', (route) => json(route, {
    signInEnabled: true,
    signedIn: state.signedIn,
    passkeysSupportedHere: true,
    source: 'internet',
    publicOrigin: null,
    tokenAccessHere: false
  }));
  await page.route('**/api/auth/register/options', (route) => json(route, {
    ceremonyId: 'reg-1',
    publicKey: {
      challenge: b64('registration-challenge'),
      rp: { id: 'localhost', name: 'GameBot' },
      user: { id: b64('user-handle-1234'), name: 'owner', displayName: 'GameBot owner' },
      pubKeyCredParams: [{ type: 'public-key', alg: -7 }],
      authenticatorSelection: { residentKey: 'required', userVerification: 'required' },
      attestation: 'none',
      excludeCredentials: []
    }
  }));
  await page.route('**/api/auth/register/complete', async (route) => {
    state.registrationBody = route.request().postDataJSON();
    state.registered = true;
    state.signedIn = true;
    await route.fulfill({ status: 204 });
  });
  await page.route('**/api/auth/sign-in/options', (route) => json(route, {
    ceremonyId: 'sign-1',
    publicKey: { challenge: b64('sign-in-challenge'), rpId: 'localhost', userVerification: 'required', allowCredentials: [] }
  }));
  await page.route('**/api/auth/sign-in/complete', async (route) => {
    state.signInBody = route.request().postDataJSON();
    state.signedIn = true;
    await route.fulfill({ status: 204 });
  });
};

test('register a passkey with a code, stay signed in after a reload, and sign in again', async ({ page, context }) => {
  const state: MockState = { signedIn: false, registered: false, registrationBody: null, signInBody: null };
  const cdp = await context.newCDPSession(page);
  await cdp.send('WebAuthn.enable');
  await cdp.send('WebAuthn.addVirtualAuthenticator', {
    options: {
      protocol: 'ctap2',
      transport: 'internal',
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true
    }
  });
  await installMock(page, state);

  // Register at /register with a code.
  await page.goto('/register');
  await page.getByLabel('Registration code').fill('abcd-efgh');
  await page.getByLabel('Device name').fill('Test phone');
  await page.getByRole('button', { name: 'Register this device' }).click();
  await expect(page.getByText('GameBot Web UI')).toBeVisible();
  expect(state.registered).toBe(true);
  expect(state.registrationBody.name).toBe('Test phone');
  expect(state.registrationBody.credential.rawId).toMatch(/^[A-Za-z0-9_-]+$/);
  expect(state.registrationBody.credential.response.attestationObject).toBeTruthy();
  expect(new URL(page.url()).pathname).toBe('/');

  // A reload keeps the session (the mock keeps signedIn).
  await page.goto('/?area=authoring');
  await expect(page.getByText('GameBot Web UI')).toBeVisible();

  // The session ends. The page shows the sign-in page and keeps the address.
  state.signedIn = false;
  await page.reload();
  await expect(page.getByRole('button', { name: 'Sign in with passkey' })).toBeVisible();
  expect(page.url()).toContain('area=authoring');
  await page.getByRole('button', { name: 'Sign in with passkey' }).click();
  await expect(page.getByText('GameBot Web UI')).toBeVisible();
  expect(state.signInBody.credential.response.signature).toBeTruthy();
  expect(state.signInBody.credential.response.userHandle).toBe(b64('user-handle-1234'));
  expect(page.url()).toContain('area=authoring');
});
