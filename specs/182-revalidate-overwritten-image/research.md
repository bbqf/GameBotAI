# Research: Revalidate an image after an overwrite

## R1. Why the browser shows the old picture

- Decision: Add `Cache-Control: no-cache` and an `ETag`.
- Rationale: A response with `Last-Modified` and no `Cache-Control` can be fresh by heuristic (a fraction of the age of the resource). `no-cache` makes the browser revalidate on each use. The 304 path already works.
- Alternatives considered: `no-store` (no reuse at all, costs a body transfer each time); `max-age=0, must-revalidate` (same effect as `no-cache` for this case, but the issue names `no-cache`); a versioned path (rejected by the issue).

## R2. ETag source

- Decision: Strong ETag from the SHA-256 hash of the bytes (first 16 bytes, hex, in quotes).
- Rationale: It changes when the bytes change, also for two overwrites with the same size and time. `UpdatedAtUtc` plus size could repeat. Images are small, so one read for the hash is cheap.
- Alternatives considered: ETag from `UpdatedAtUtc` ticks and size (cheap, but can repeat for two quick overwrites); store a hash in the image metadata (needs a repository and store change, out of scope).

## R3. 304 handling

- Decision: Use `Results.File` with `entityTag` and `lastModified`. The framework applies the precondition headers.
- Rationale: It gives correct handling of `If-None-Match` (list, `*`, weak compare), `If-Modified-Since`, and ranges. The current code (`Results.Stream` with `lastModified`) already returns 304 for `If-Modified-Since` by the same mechanism.
- Alternatives considered: Manual header parsing (more code, more risk).

## R4. Where the `Cache-Control` header goes

- Decision: Set it on `HttpContext.Response.Headers` before the result runs, so it is on the 200 and on the 304 answer.
- Rationale: RFC 9110 asks a 304 to repeat the cache headers of the 200.
- Alternatives considered: Set it only on the 200 (a 304 would then not refresh the stored rule).

## R5. Handler location

- Decision: `GetImageAsync` in `src/GameBot.Service/Endpoints/ImageReferencesEndpoints.cs`. The handler is bound to `GET /api/images/{id}` at line 63.
