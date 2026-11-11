# Contract: Web UI

Phone support level of all parts: `full`. All parts follow `docs/mobile-ui-principles.md` and work at 375 px.

## U-1. Access gate

- At start, the application calls `GET /api/auth/status`.
- `signInEnabled` true, `signedIn` false, `passkeysSupportedHere` true → show the sign-in page (U-2) in place of the content. The address does not change.
- `signInEnabled` true, `signedIn` false, `passkeysSupportedHere` false → show the text "Passkey sign-in works only at the public address." and a link to `publicOrigin` when it is not null. When `tokenAccessHere` is true, also show the current token prompt (FR-031). When it is false, show no token prompt.
- A later `401` with `signInEnabled` true → show the sign-in page again. After the sign-in, the same address shows, and the application loads its data again. It does not send the failed action again.
- A `403` with code `remote_access_refused` → show the text "This service does not accept connections from the internet." and no sign-in control.
- The path `/register` shows the registration page (U-3), also with no session.

## U-2. Sign-in page

- Title "GameBot". One primary button: "Sign in with passkey".
- No password field. No registration link. No user name field (FR-004).
- Failure text, the same for each cause: "Sign-in did not work. Try again."
- If the browser has no passkey support: "This browser does not support passkeys." and the button is off.
- When the owner cancels the system dialog, the page shows no error.

## U-3. Registration page (`/register`)

- Field "Registration code" (format `XXXX-XXXX`, upper case, no auto-correct) and optional field "Device name" (maximum 40 characters).
- Button "Register this device".
- Help text: "Make a code on the GameBot computer with the command `GameBot.Service.exe auth new-code`."
- Failure text, the same for each cause: "Registration did not work. Make a new code and try again."
- Success: the application opens the start page, signed in.

## U-4. Sign-in devices section (Configuration area)

Shown only when `signInEnabled` is true.

- A list of passkeys: name, "Registered" date, "Last used" time, and a "This device" mark.
- For each item: "Rename" and "Remove". "Remove" asks for confirmation. The confirmation for the passkey of this device and for the last passkey gives a warning that the owner will need the GameBot computer to register again.
- Button "Sign out" (this device).
- Button "Sign out all devices", with confirmation.
- Help text that tells how to add a device (the command of U-3).
- Touch targets are at least 44 px. The list is one column on a phone.

## U-5. Unchanged

- With the sign-in function off, the UI is the same as before this feature, the token prompt included.
