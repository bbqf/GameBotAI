# Research: Disk-backed Reference Image Storage

## Decisions

- Use `System.IO` for disk I/O (no new packages).
- Persist under `data/images` with file names `{id}.{ext}`; default `.png` if absent.
- Validate `id` with regex `^[A-Za-z0-9_-]{1,128}$`; reject path traversal.
- Atomic writes: write to `data/images/.tmp/{id}.{ticks}.{ext}` then `File.Move` to final.
- Startup load: enumerate `data/images/*.{png,jpg,jpeg}` and cache metadata; load on-demand to avoid large memory footprint.
- Endpoints: `POST /images` (upload), `GET /images/{id}` (exists/resolve), `DELETE /images/{id}` (remove).
- Evaluator resolution: prefer disk-backed store; return Pending with `reference_missing` when absent.

## Rationale

- Avoid new dependencies per project policies; `System.IO` handles needs reliably.
- Simple file structure in `data/images` improves auditability and backup.
- Atomic writes prevent partial/corrupted files under concurrent uploads.
- On-demand loading reduces startup latency and memory usage for many images.
- Clear endpoints match existing API style and make operations testable.

## Alternatives Considered

- Database (LiteDB, SQLite): Adds complexity and external packages; not needed for scope.
- In-memory only with periodic dumps: Risk of data loss on crash; conflicts with persistence requirement.
- Always load all bytes at startup: Slower startup and higher memory; unnecessary for image-match evaluation which can stream.