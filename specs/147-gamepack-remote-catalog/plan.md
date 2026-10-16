# Implementation Plan: Browse and install gamepacks from a remote catalog

**Branch**: `147-gamepack-remote-catalog` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/147-gamepack-remote-catalog/spec.md` (with the corrected clarifications and FR-001 to FR-019)

## Summary

The operator sets one catalog location (a web address or a local folder), an optional bearer token, and an optional "allow insecure" flag. The catalog is one `index.json` document plus file downloads. Each index entry lists the version, the changelog text, the gamepack file location, an optional SHA-256 hash, and an optional detached signature file location.

The service has three new operations: settings (read and write), list, and install. List reads the index on each request (no cache) and marks the installed version and each version that is newer. Install checks that the entry is listed (reason `entry_not_listed`, before any download), downloads the gamepack file and the optional signature file with the same rules, compares the hash when the entry has one, and calls the existing `GamepackService.InstallAsync` with the signature text. The signature policy of feature 146 and the install flow of feature 143 stay as they are. A dry run does everything except the change.

A new "Gamepacks" page in the web UI shows the list, the changelog text, the installed marker, an Update button for each newer version, a "dry run" check box, and a "Refresh" button. Nothing installs unless an operator action starts it. No hosted service, no timer, and no cache use the catalog types.

Technical approach (details in [research.md](research.md)):

- Sources: `HttpCatalogSource` and `FolderCatalogSource` behind one `IGamepackCatalogSource`. A factory picks the source from the current settings on each request (no restart, FR-003).
- HTTP: `IHttpClientFactory`, `AllowAutoRedirect = false`, at most 3 manual redirects to the same host and scheme, the bearer header only for the catalog host, 30 s per request.
- Limits: index 5 MB, gamepack file 100 MB (the existing limit), signature file 1 MB. Counted stream reads and an early `Content-Length` check.
- Safe paths: a file location must stay under the catalog base (no "..", no absolute path, no other host, no backslash).
- Settings: one JSON file `gamepack-catalog.json` in the data root. The token is never returned or logged. The read route returns `tokenSet`.
- Reasons: a fixed list of failure codes. A missing `index.json` gives `index_invalid`. An unlisted version gives `entry_not_listed`.
- One application log message for each list and each install, with name, version, and outcome code, and no token (FR-019).

## Technical Context

**Language/Version**: C# on .NET (the version of the existing solution); TypeScript and React in `src/web-ui`
**Primary Dependencies**: ASP.NET Core minimal endpoints, `System.Net.Http` through `IHttpClientFactory`, `System.Text.Json`, `System.Security.Cryptography` (SHA-256 and fixed-time compare), Swashbuckle (schema filter). No new package.
**Storage**: One new JSON file `gamepack-catalog.json` in the data root (location, allowInsecure, token). The platform keeps no copy of the index.
**Testing**: xUnit. Unit tests (location rules, index parser, settings store, folder source, DI registration check). Contract tests (routes, status codes, token canary, API token rule, log capture, missing index). Integration tests (local test HTTP server, install, dry run, hash, failure state compare, parallel install, limits, timing). Jest for the page.
**Target Platform**: Windows service (GameBot.Service) and the web UI
**Project Type**: Web service with a web UI page
**Performance Goals**: A list of 50 gamepacks with 10 versions each shows in under 5 seconds (SC-002). Operator walk in under 3 minutes (SC-001).
**Constraints**: Nothing changes when the catalog fails. The token never appears in a response or a log. The token goes only to the catalog host. No automatic install. `Program.cs` stays thin.
**Scale/Scope**: One catalog for each instance. Up to a few hundred index entries. One new page.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

*NON-NEGOTIABLE*: If `build` or required `test` runs are failing (local or CI), implementation progression is blocked until failures are fixed or a documented maintainer waiver exists. Status: acknowledged. The final task runs the full build and test suites.

*NON-NEGOTIABLE*: All text in this plan and in the artifacts it produces obeys Simplified Technical English (Principle VI). Status: pass.

*Phone check (Principle III)*: The "Gamepacks" page has phone support level **full**. It follows `docs/mobile-ui-principles.md` (phone layout, touch targets). Task T044 checks it.

| Principle | Result | Note |
|-----------|--------|------|
| I. Code quality | Pass | Small classes with one job. Handlers are named methods (taint analyzers). `Program.cs` stays thin: the wiring goes in `GamepackServiceCollectionExtensions.cs`. |
| II. Testing | Pass | A test for each requirement and success criterion. See "Test Strategy and Task Notes" and [quickstart.md](quickstart.md). |
| III. User experience | Pass | New page, phone level full. Failure reasons show in plain words. API text and OpenAPI descriptions are in STE. |
| IV. Performance | Pass | Goals above, with a timing test (SC-002) and a timed manual walk (SC-001). |
| V. Living documentation | Pass | Tasks update `docs/gamepacks.md`, `docs/architecture.md` ("Last reviewed"), `specs/STATUS.md`, `CHANGELOG.md`, and the spec Status. |
| VI. STE | Pass | All artifacts, messages, and comments in STE. |
| Version bump | Pass | The last task bumps `patch` in `installer/versioning/version.override.json` before the push. |

Post-design re-check (after Phase 1): no violation. No entry in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/147-gamepack-remote-catalog/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── catalog-index.md          # Phase 1 output: index document and file rules
│   └── gamepack-catalog-api.md   # Phase 1 output: routes and errors
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── GameBot.Domain/
│   └── Gamepacks/
│       └── Catalog/                              # new
│           ├── CatalogFailure.cs                 # reason codes + CatalogException
│           ├── CatalogOptions.cs                 # limits, timeout, redirects
│           ├── GamepackCatalogSettings.cs        # location, allowInsecure, token
│           ├── IGamepackCatalogSettingsStore.cs
│           ├── GamepackCatalogSettingsStore.cs   # JSON file, atomic write, read on each call
│           ├── CatalogEntry.cs
│           ├── CatalogLocation.cs                # scheme rules, base, safe relative path
│           ├── GamepackCatalogIndexParser.cs
│           ├── IGamepackCatalogSource.cs
│           ├── FolderCatalogSource.cs
│           ├── HttpCatalogSource.cs
│           ├── GamepackCatalogSourceFactory.cs
│           └── GamepackCatalogService.cs         # List and Install
└── GameBot.Service/
    ├── ApiRoutes.cs                              # add GamepackCatalog constants
    ├── Endpoints/
    │   ├── GamepackServiceCollectionExtensions.cs   # register catalog services (no hosted service)
    │   ├── GamepackCatalogEndpoints.cs           # new: settings, list, install
    │   └── Dto/GamepackCatalogDtos.cs            # new
    └── Swagger/GamepackSchemaFilter.cs           # descriptions for the new types
└── web-ui/src/
    ├── services/gamepackCatalog.ts               # new: API client
    ├── pages/GamepacksPage.tsx                   # new: page
    ├── pages/GamepacksPage.css                   # new
    ├── pages/__tests__/GamepacksPage.test.tsx    # new
    ├── types/navigation.ts                       # add area id `gamepacks`, path `/gamepacks`, phone `full`
    └── App.tsx                                   # area parse, renderGamepacks, switch case

tests/
├── unit/Gamepacks/
├── contract/Gamepacks/
└── integration/Gamepacks/

docs/gamepacks.md                                 # catalog section
```

**Structure Decision**: Domain logic in `GameBot.Domain/Gamepacks/Catalog/`, HTTP in `GameBot.Service/Endpoints/`.

**Web UI route and navigation**: The web UI has no router library. The page is a new navigation area. The route is `/gamepacks` (area id `gamepacks`, phone support `full`). The actual files are:

- `src/web-ui/src/types/navigation.ts`: the area id and the navigation entry.
- `src/web-ui/src/App.tsx`: the area parse from the path, `renderGamepacks`, and the switch case (same pattern as `UpdatePage`).
- Navigation tests to update: `src/web-ui/src/types/__tests__/navigation.test.ts`, `src/web-ui/src/components/__tests__/Navigation.test.tsx`, `src/web-ui/src/__tests__/navigation.spec.tsx`, and `src/web-ui/src/components/phone/__tests__/PhoneTabBar.test.tsx`.

## Test Strategy and Task Notes

- Each requirement has one test owner. The map is in [quickstart.md](quickstart.md).
- Order of work: failure codes, options, settings, location rules, parser, settings store, sources, factory, service, DTOs, endpoints, DI wiring, then the page.
- FR-012 limits: three separate tests: an oversize signature file (over 1 MB), an oversize gamepack file, and a per-request timeout on a file download. Tests use small option values.
- FR-014: a correct hash passes and a wrong hash gives `hash_mismatch`.
- FR-015: a dependency-injection registration check. It builds the real registration and follows the constructor graph transitively (all depth) from each `IHostedService`, `BackgroundService`, and timer-type registration. If any path reaches a catalog type, the test fails. This includes an indirect dependency. The test does not scan source text. Task T038 does this check.
- FR-008 signature source: tests cover an entry with a signature file (passes to the check), an entry with none (the check sees "missing"), and a signature download that fails the same way as a file download.
- Edge case parallel install: two parallel catalog installs of one name follow the feature 143 rules.
- Missing `index.json`: folder and web both give `index_invalid` (HTTP 502).
- FR-016 page tests (Jest): the "dry run" check box turns the install button into a dry run, and the "Refresh" button reloads the list. Neither runs without an operator action.
- SC-003: a state snapshot (registry, sequences, commands, images, settings) before and after each failing request must be equal.
- SC-004 and FR-019: a log capture test and a token canary scan of all responses. Open the logging gate component first (see test-harness notes).
- Contract tests share a bin data directory. Use a temp data root for each test host.
- Keep the existing gamepack tests unchanged.
- Docs tasks and the patch bump task come last.

## Complexity Tracking

No constitution violation. This table is empty.
