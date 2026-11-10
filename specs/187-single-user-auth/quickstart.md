# Quickstart: Single-Owner Sign-In for Internet Access

This file shows how to try the feature after the implementation. The owner guide is `docs/internet-access.md`.

## Before you start

- A public domain name with HTTPS on a reverse proxy (Traefik or nginx) on a different host of the home network. The proxy forwards to `http://<service machine address>:8080`. The service machine is not exposed to the internet.
- The service listens on a home network address (`GAMEBOT_BIND_HOST`, see `docs/phone-access.md`).
- Recommended: a firewall rule on the service machine that permits port 8080 only from the proxy host and from your home network devices.
- Recommended for nginx: set `Host`, `X-Forwarded-For`, `X-Forwarded-Proto`, and `X-Forwarded-Host`. Traefik sends them by default.

## Set up

1. Set the environment variables of the service:
   - `GAMEBOT_AUTH_ENABLED=true`
   - `GAMEBOT_AUTH_PUBLIC_ORIGIN=https://bot.example.com`
   - `GAMEBOT_AUTH_TRUSTED_PROXIES=<proxy address or range>`, for example `192.168.1.5`. This setting is mandatory for a proxy on a different host. For a gateway on the service machine, set `127.0.0.1,::1`.
2. Restart the service.
3. On the service machine, run `GameBot.Service.exe auth new-code`. Note the code.
4. On the phone, open `https://bot.example.com/register`.
5. Enter the code and a device name. Tap **Register this device**. Confirm with the screen lock.
6. The start page opens. The phone is signed in.

## Check the acceptance scenarios

| Check | Steps | Expected |
|-------|-------|----------|
| Setup time (SC-012) | Measure the time of the "Set up" steps, from step 1 to step 6. | Less than 30 minutes. |
| Registration time (SC-002) | Measure the time from step 3 to the end of step 6. | Less than 2 minutes. |
| Sign-in (US1, SC-001) | Open Configuration, tap **Sign out**. Tap **Sign in with passkey**. | The same page opens in less than 15 seconds. |
| Refusal (US2) | In a private browser window, call `https://bot.example.com/api/queues`. | `401`, code `unauthorized`, no data. |
| No registration without code (US2) | Open `/register` and enter `AAAA-AAAA`. | "Registration did not work." No new passkey in `auth list`. |
| Code is one-time (US3) | Use the code of step 3 again on a second device. | Refusal. |
| Token is not for the internet | From a phone on a mobile network, send the correct bearer token to the public address. | `401`. On the service machine at `http://localhost:8080` and on a home network device at `http://<service machine address>:8080`, the token works. |
| API documentation | Open `https://bot.example.com/swagger` with no session. | `401`. |
| Stay signed in (US5) | Restart the service. Open the UI on the phone. | No sign-in. |
| End all sessions (US6) | Sign in on two devices. Tap **Sign out all devices** on one. | The two devices show the sign-in page. |
| Lost device | Run `auth list`, then `auth remove <id>`. | That device shows the sign-in page in about 1 second and cannot sign in. |
| Token through the proxy (US4) | From the proxy host, send the correct bearer token to `http://<service machine address>:8080/api/queues`. | `401`. |
| Proxy changes the host name (US4) | In nginx, remove the `proxy_set_header` lines. Register and sign in again. | The two flows work. |
| Direct home network use (US4) | On a home network device, open `http://<service machine address>:8080`. | The text "Passkey sign-in works only at the public address.", the link, and the token prompt. No data. After you enter the token, the UI works. |
| Sign-in function off (FR-023) | Set `GAMEBOT_AUTH_ENABLED=false`, restart, open the public address from a phone on a mobile network. | `403`, code `remote_access_refused`. The UI on the home network works. |

**Mandatory check for each new proxy or gateway**: do the last row before you set the sign-in function on. If the phone gets data, the service does not know that the request came from the internet. Set `GAMEBOT_AUTH_TRUSTED_PROXIES` to the address of the proxy first (research R6).

## Automated tests

```powershell
dotnet test "C:\src\GameBot\tests\unit\GameBot.UnitTests.csproj" --filter "FullyQualifiedName~Auth"
dotnet test "C:\src\GameBot\tests\integration\GameBot.IntegrationTests.csproj" --filter "FullyQualifiedName~Auth"
dotnet test "C:\src\GameBot\tests\contract\GameBot.ContractTests.csproj" --filter "FullyQualifiedName~Auth"
npm --prefix "C:\src\GameBot\src\web-ui" test -- auth
```
