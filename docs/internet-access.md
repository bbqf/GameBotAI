# Use GameBot from the internet

This guide shows how to open the GameBot web UI from the internet. Only the owner can sign in. The sign-in uses a passkey. A passkey is a key on your phone or computer. You unlock it with your screen lock, for example a fingerprint.

The sign-in function is off by default. While it is off, the service refuses each request from the internet.

> **Warning.** This guide gives a way to reach the service from the internet. Read all of it before you start. A mistake can open the service to other persons. The mandatory check in "Check from a mobile network" shows if the setup is safe.

## How it works

- You put a reverse proxy in front of the service. The proxy gives HTTPS and a public domain name.
- The service listens on plain HTTP in your home network. The proxy connects to it.
- The service treats each request from the proxy as an internet request. For such a request, the service needs a session. A session starts with a passkey sign-in.
- The access token (`GAMEBOT_AUTH_TOKEN`) never works for an internet request. It works only on the service computer and in your home network.
- You must tell the service the address of the proxy (`GAMEBOT_AUTH_TRUSTED_PROXIES`). With the sign-in function on and no trusted proxy, the service trusts only the service computer. It treats each other request as an internet request.
- You register each new device with a one-time code. Only a command on the service computer makes this code. No web page makes a code.

## Prerequisites

1. A public domain name that points to your router, for example `bot.example.com`.
2. A reverse proxy with a valid HTTPS certificate. Traefik and nginx work. The proxy runs on a different computer of your home network, or on the service computer.
3. A port forward on the router from port 443 to the proxy.
4. GameBot with the feature "single-owner sign-in" (this version).

## Settings

Set these environment variables on the service computer. Then restart the service.

| Variable | Value | Notes |
|----------|-------|-------|
| `GAMEBOT_AUTH_ENABLED` | `true` | Turns the sign-in function on. The default is `false`. |
| `GAMEBOT_AUTH_PUBLIC_ORIGIN` | `https://bot.example.com` | The public address. It must use `https`. The passkeys are bound to the host of this address. |
| `GAMEBOT_AUTH_TRUSTED_PROXIES` | `192.168.1.5` | The address of your proxy. You can give more than one address and CIDR ranges, for example `192.168.1.5,172.18.0.0/16`. **This setting is mandatory when the sign-in function is on.** For a gateway on the service computer, use `127.0.0.1,::1`. |
| `GAMEBOT_AUTH_TOKEN` | a long random value | Optional. Use it to open the web UI at the direct address in your home network. In the home network, the token works only when `GAMEBOT_AUTH_TRUSTED_PROXIES` is set. |

The same settings exist as `Service:Auth:Enabled`, `Service:Auth:PublicOrigin`, `Service:Auth:TrustedProxies`, and `Service:Auth:Token` in the service configuration. The web UI and the API cannot change these settings. The Configuration page shows them with their source.

### Set the settings in a file (no environment variables)

You can use a file in place of environment variables.

1. Make a file named `appsettings.json` in the install folder of the service. This is the folder that holds `GameBot.Service.exe`. The file is optional. The service reads it at start. The `GameBot.Service.exe auth ...` command reads it too.
2. Put the settings in the file. Use text values:

   ```json
   {
     "Service": {
       "Auth": {
         "Enabled": "true",
         "PublicOrigin": "https://bot.example.com",
         "TrustedProxies": "192.168.1.5",
         "Token": "a-long-random-value"
       }
     }
   }
   ```

3. Restart the service.

Notes:

- A value in the file wins over the environment variable of the same setting.
- The Configuration page shows a setting from the file with the source `File`.
- `Token` is a secret. A person who can read the file can read the token. Limit the access to the file.
- Do not set these settings on the Configuration page. The page refuses the change. The service takes the sign-in settings only from the file and the environment.
- If a setting has no effect, read the startup log entry (category `GameBot.Service.Security`). It lists the settings that the service ignored.

The service makes a startup log entry (category `GameBot.Service.Security`) with the sign-in state, the public origin, and the trusted proxies. It writes a critical entry when the sign-in function is on and no trusted proxy is set.

### Sign-in function on and no trusted proxy

If the sign-in function is on and `GAMEBOT_AUTH_TRUSTED_PROXIES` is empty, the service cannot tell a request of the proxy from a request of a home network device. Thus the service uses the safe result:

- Only a request from the service computer (loopback address) is a local request. The access token works there.
- Each other request is an internet request. It needs a session from a passkey sign-in. The access token does not work, also in your home network.
- The web UI at the direct address in your home network shows no token prompt.

To get the access with the token in your home network back, set `GAMEBOT_AUTH_TRUSTED_PROXIES` and restart the service. With the sign-in function off, this rule does not apply, and the home network works as before.

## Primary layout: a proxy on a different host

The proxy runs on a different computer of your home network. The service computer is not open to the internet.

1. Set the listen address of the service to an address of your home network (`GAMEBOT_BIND_HOST`, see `docs/phone-access.md`).
2. Set `GAMEBOT_AUTH_TRUSTED_PROXIES` to the address of the proxy. This step is mandatory.
3. Add a firewall rule on the service computer. It must permit the service port only from the proxy computer.
4. The hop from the proxy to the service uses plain HTTP in your home network. A person who can read your home network traffic can read the session cookie on this hop. Use this setup only on a network that you trust.

### Traefik example

Not tested with a real proxy.

```yaml
http:
  routers:
    gamebot:
      rule: Host(`bot.example.com`)
      entryPoints: [websecure]
      tls:
        certResolver: letsencrypt
      service: gamebot
  services:
    gamebot:
      loadBalancer:
        servers:
          - url: http://192.168.1.20:5081
```

Traefik keeps the public `Host` header and adds the `X-Forwarded-*` headers by default.

### nginx example

Not tested with a real proxy.

```nginx
server {
  listen 443 ssl;
  server_name bot.example.com;
  # ssl_certificate and ssl_certificate_key lines here

  location / {
    proxy_pass http://192.168.1.20:5081;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-Host $host;
  }
}
```

The default `proxy_pass` of nginx sends no forward header and changes the `Host` header to the address of the service. Such a request looks the same as a request from a home network device. Only the setting `GAMEBOT_AUTH_TRUSTED_PROXIES` tells them apart. The four `proxy_set_header` lines above also give the service the real client address for its limit on failed attempts.

## Use at the direct address in the home network

With the sign-in function on, a request at the direct address (for example `http://192.168.1.20:5081`) has no passkey support, because passkeys work only at the public address. This section applies when `GAMEBOT_AUTH_TRUSTED_PROXIES` is set. If it is not set, see "Sign-in function on and no trusted proxy". At the direct address:

- The web UI shows the public address as a link.
- If you set `GAMEBOT_AUTH_TOKEN`, the web UI also shows a token prompt. After you enter the token, you can use the web UI.
- If you set no token, you must use the public address.

The token is sent in clear text over plain HTTP. See the warning in `docs/phone-access.md`.

### Router with no NAT loopback

Some routers cannot send a request for your public domain name from the home network back into the home network. If your phone cannot open `https://bot.example.com` on your Wi-Fi, add a local DNS entry on the router. The entry must point `bot.example.com` to the address of the proxy. Or use mobile data when you are at home.

## Alternative layout: a gateway on the service computer

The proxy runs on the same computer as the service. It connects from `127.0.0.1`.

1. Set `GAMEBOT_AUTH_TRUSTED_PROXIES` to `127.0.0.1,::1`.
2. Keep the listen address of the service on the loopback address, or set a firewall rule.

With this setting, each request that comes through the loopback address is an internet request. The local command `GameBot.Service.exe auth ...` does not use a web request, so it still works. The access token works only in your home network in this layout.

## Check from a mobile network (mandatory)

Do this check before you rely on the setup.

1. Turn off Wi-Fi on your phone. Use mobile data.
2. With the sign-in function **off**, open `https://bot.example.com`. You must see "This service does not accept connections from the internet." An API call, for example `https://bot.example.com/api/queues`, must get `403` and the answer `{"error":{"code":"remote_access_refused",...}}`. With the function off, the service refuses each internet request with `403`, also with a passkey or a token. If you see the web UI, stop. The service does not see the proxy as a proxy. Check `GAMEBOT_AUTH_TRUSTED_PROXIES`.
3. Turn the sign-in function on. Open the same address. You must see the sign-in page and no data.
4. With the sign-in function **on**, open `https://bot.example.com/api/queues`. You must get `401` and the answer `{"error":{"code":"unauthorized",...}}`.
5. With the sign-in function **on**, send the access token from the phone as a bearer header to `https://bot.example.com/api/version`. You must get `401`. The token must not work from the internet.

The two answers are different on purpose. `403` (`remote_access_refused`) means the function is off. `401` (`unauthorized`) means the function is on and the request has no valid session.

## Register a device

1. On the service computer, run:

   ```text
   GameBot.Service.exe auth new-code
   ```

   The command prints a code like `K7QF-9XWM`. The code is valid for 10 minutes. You can use it one time. A new code cancels an older code.
2. On the new device, open `https://bot.example.com/register`.
3. Enter the code and an optional device name. Tap "Register this device". Unlock the passkey with your screen lock.
4. The web UI opens. The device has a session.

The service cancels the code after 5 wrong attempts. The service blocks a source address for 15 minutes after 5 failed attempts in 5 minutes.

A synced passkey (Google Password Manager, iCloud Keychain) works on all devices of that account after one registration. The service shows it as one device.

### Android phone with no Google account (KeePassDX)

An Android phone needs a passkey provider to store the passkey. Google Password Manager needs a Google account. If you do not want to use a Google account, use KeePassDX. KeePassDX keeps the passkey in a file on the phone. It has no cloud account.

Before you start:

- The phone must run Android 14 or newer.
- Install KeePassDX and make a database in it.
- Update Chrome.

Set up the phone one time:

1. Open the Android settings: Passwords, passkeys & accounts.
2. Set KeePassDX as the preferred service.

Register the phone:

1. Make a code on the service computer (`GameBot.Service.exe auth new-code`).
2. In Chrome on the phone, open `https://bot.example.com/register`. Enter the code. Tap "Register this device".
3. The system dialog opens. Do **not** tap "Create" at once. Tap "More options", and then tap "Create" in the list that opens. KeePassDX opens and saves the passkey. If you tap "Create" at once, the registration can fail.
4. Unlock the passkey with your screen lock.

Notes:

- The passkey is only on the phone, in the KeePassDX file. Make a backup of the file. If you lose the file, register the phone again.
- If the page shows "Registration did not work" and you see no other error, the phone could not store the passkey. Check that a provider is selected in the Android settings.
- A phone with Chrome and no passkey provider can fail with the browser error `NotReadableError`. The cause is on the phone, not in the service.

## Sessions

A session ends 30 days after its last use. A restart of the service does not end sessions. Use "Sign-in devices" in the Configuration area to sign out, to sign out all devices, to rename a device, and to remove a device.

## Lost device

If you lose a device, do these steps on the service computer:

```text
GameBot.Service.exe auth list
GameBot.Service.exe auth remove <id>
GameBot.Service.exe auth end-sessions
```

`auth remove` removes the passkey and ends its sessions. `auth end-sessions` ends all sessions. The change is in effect in about 1 second, also when the service runs. If you lose all devices, make a new code with `auth new-code` and register again.

## Change the domain name

A passkey works only at the domain name where you registered it. If you change the domain name, change `GAMEBOT_AUTH_PUBLIC_ORIGIN`, remove the old passkeys with `auth remove`, and register again.

## Sign-in record

The service writes each sign-in event to `data/auth/sign-in-records.jsonl`: sign-in, failed sign-in, sign-out, registration, removal, blocked source, and refused internet request. The file has no token, no code, and no cookie. It rolls at 1 MB to `sign-in-records.1.jsonl`. The same events go to the log category `GameBot.Service.Security`.

The backup and restore functions do not include `data/auth/`.

## Residual risk

The service cannot always tell where a request comes from. Two setups hide the source of a request:

- A proxy on a different host that sends no forward header, when `GAMEBOT_AUTH_TRUSTED_PROXIES` is not set.
- A gateway on the service computer that sends no forward header and changes `Host` to a loopback name.

The setting `GAMEBOT_AUTH_TRUSTED_PROXIES` closes these two cases.

- With the sign-in function on and no trusted proxy, the service treats each request that does not come from the loopback address as an internet request. Thus the first case needs a session, and the access token does not help.
- For the second case, you must set `127.0.0.1,::1`. If you forget it, the service takes the request for a request from the service computer. Such a request passes only with the correct access token, and wrong tokens count for the limit on failed attempts.
- With the sign-in function off, the service takes a request of these two setups for a home network request, and such a request passes. The check from a mobile network shows this problem.

The firewall rule for the service port also protects you.
