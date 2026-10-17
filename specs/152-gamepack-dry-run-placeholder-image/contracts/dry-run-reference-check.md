# Contract: dry run image reference check

Endpoint: `POST /api/gamepacks/install?dryRun=true` (and `takeOver=true`). The request and response shapes do not change.

## Behavior change

| Sequence image id | Sequence declares the parameter | Result |
|-------------------|---------------------------------|--------|
| `{{name}}` (whole, after trim) | `name` | No problem for the id |
| `{{name}}` | not `name` (or none) | `reference` problem |
| `{{name}}` | `Name` (other letter case) | `reference` problem |
| `pre-{{name}}`, `{{name}}-x` | `name` | `reference` problem if the id is not a target |
| `{{ name }}` | `name` | `reference` problem (not a whole placeholder) |
| `{{name}}` | no parameter list (null or empty) | `reference` problem |
| `{{iteration}}` (built-in) | none | `reference` problem |
| `{{name}}` in a command image id | any | `reference` problem (commands are not filtered) |
| plain id in file or installation | any | No problem |
| plain id not in file or installation | any | `reference` problem |

Problem message text does not change: `Image '<id>' is not in the gamepack or in the installation.`

## Unchanged

- Command image checks, command reference checks, seed template checks.
- `POST /api/sequences` validation and queue template save checks.
- Install result for a valid gamepack.
