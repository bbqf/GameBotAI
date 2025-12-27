# Research — Web UI Authoring (MVP)

## Decisions

- Frontend framework: React + Vite + TypeScript
  - Rationale: Fast dev, broad ecosystem, good DX; minimal tooling overhead.
  - Alternatives: Blazor WASM (tighter .NET integration, heavier payload), Next.js (SSR not needed), plain HTML/JS (slower dev, less structure).

- Auth token persistence: Memory-only by default; optional "remember token" in localStorage
  - Rationale: Reduces exposure risk while allowing opt-in convenience.
  - Alternatives: Always persist (riskier), sessionStorage only (less convenient across tabs).

- Mobile support: Minimum 375px viewport; no PWA in MVP
  - Rationale: Keep MVP scope tight; responsive layout sufficient.
  - Alternatives: Full PWA (offline, installable), device-specific breakpoints.

- API Base URL: Configurable; same-origin by default
  - Rationale: Local dev parity; supports proxying or direct service port.
  - Alternatives: Hard-coded URL (inflexible), env-only (less discoverable for users).

- Validation display: Surface service 400 errors with field-level mapping
  - Rationale: Keeps UI logic simple; defers rules to service.

## Patterns & Practices

- State: Keep forms local and use small utility signals for config/token.
- Accessibility: Labeled inputs, adequate tap targets, WCAG AA contrast.
- Security: Do not log tokens; avoid storing unless opted in.
- Performance: Target p95 < 1.5s for create/edit roundtrip in local environment; error rendering < 300ms.
- Observability: Use browser console only; service provides structured logs.
