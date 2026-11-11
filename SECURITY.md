# Security Policy

## Supported Versions


| Version | Supported          |
| ------- | ------------------ |
| 1.x.x   | :white_check_mark: |

## Access control

- The service has an optional single-owner sign-in with passkeys (feature 187). It is off by default. While it is off, the service refuses each request from the internet. The access token works only on the service computer and in the home network when the sign-in function is on.
- Do not open the service port to the internet without the sign-in function. Read [docs/internet-access.md](docs/internet-access.md) before you expose the service. The guide has a mandatory check from a mobile network.
- The sign-in settings (`GAMEBOT_AUTH_ENABLED`, `GAMEBOT_AUTH_PUBLIC_ORIGIN`, `GAMEBOT_AUTH_TRUSTED_PROXIES`, `GAMEBOT_AUTH_TOKEN`) come only from the service configuration and the environment. The web UI and the API cannot change them.
- The service keeps only hashes of session tokens and registration codes. It writes no secret to a log or to the sign-in record.

## Reporting a Vulnerability

Submit an [Issue](https://github.com/bbqf/GameBotAI/issues) with this repository, usually it will get fixed within couple of days
