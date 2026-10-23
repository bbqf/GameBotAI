# Phone Access

This guide shows how to open the GameBot web UI on a phone. It takes about 10 minutes.

## Before you start

- A default install listens on `127.0.0.1:8080`. A phone cannot connect to this address. You must change the listen address first (step 1).
- The phone and the PC must be on the same network.

## Steps

1. **Set the listen address.** Select `all interfaces (0.0.0.0)` in the **Bind interface** list of the installer (it shows on every install and upgrade). You can also set `BIND_HOST=0.0.0.0` on the command line. The installer saves the choice in `config\network.json`, so an upgrade keeps it. You can also set the environment variable `GAMEBOT_BIND_HOST=0.0.0.0` and restart the service. See `INSTALL.md` for the installer options.
2. **Open the firewall port.** Allow inbound TCP connections on the GameBot port (8080 by default) in Windows Firewall. Use the profile of your home network (Private).
3. **Set an access token.** Set the environment variable `GAMEBOT_AUTH_TOKEN` to a long random value. Restart the service.
4. **Find the PC address.** Run `ipconfig` on the PC. Note the IPv4 address, for example `192.168.1.20`.
5. **Open the UI.** On the phone, open `http://<PC address>:8080` in Safari (iPhone) or Chrome (Android).
6. **Enter the token.** The UI shows "Access token needed. Open Configuration." Tap **Open Configuration**. Paste the token in the **Bearer Token** field. Turn on **Remember token** so the phone keeps it.
7. **Add the page to the home screen.**
   - iPhone: tap **Share**, then **Add to Home Screen**.
   - Android: tap the browser menu, then **Add to Home screen** (or **Install app**).

The UI then opens in its own window, without the browser bar.

## What works on a phone

| Area | Phone support |
|------|---------------|
| Queues | Reduced: status, start, stop, and the entry switches |
| Execution | Full |
| Notifications | Full |
| Configuration | Full |
| Update | Full |
| Authoring | Desktop only |
| Execution Logs | Desktop only |

A desktop-only area shows the notice "Use a larger screen for this area."

## Security

> **Warning 1.** A service without a token is open to everyone on the network. Always set `GAMEBOT_AUTH_TOKEN` before you set the listen address to `0.0.0.0`.

> **Warning 2.** The token travels in clear text over plain HTTP. Anyone who can see the network traffic can read it. Use this setup only on a network that you trust.

> **Warning 3.** Do not open the port to the Internet. Outside your home network, use a private VPN link to reach the PC.

## Limits of plain HTTP

Browsers allow service workers and the full install prompt only on secure (HTTPS) origins. A GameBot service on a home network uses plain HTTP. For this reason:

- The home-screen shortcut and the standalone window work.
- Offline mode does not work. The UI needs the service to show data.
- The browser can show **Add to Home screen** and not **Install app**.

A private HTTPS link (for example through a VPN or a tunnel that gives a certificate) removes these limits. This is an optional step.

## When the connection is lost

If the phone cannot reach the service, the UI shows "Connection lost". The Start, Stop, and save controls are off. The UI does not send an action again when the connection returns. It only loads the data again.

## Follow-up idea

A later change can refuse to start on a non-local address when no token is set. This guide does not need that change.
