# Quickstart: Idle-Pause the Game During Queue Gaps; Retire the MCP Server

> This branch ships two independent changes. Part A (below) is the idle-pause feature. Part B is the
> full removal of the project's MCP server — see the last section.

## Part A — Idle-pause

## What it does

When enabled on a queue, the runtime backs the game out to the device home screen whenever the wait
until the next scheduled sequence exceeds the idle threshold (default 30s), and brings the game back the
moment the next sequence is due. The live monitor shows an explicit "Idle Pause" state (with a resume
time) for the whole gap, so an idle queue never looks hung. The pause is watchdog-exempt and writes
nothing to the execution log.

## Enable it on a queue (API)

```bash
# Turn on idle-pause with a 30s threshold
curl -X PUT http://localhost:8080/api/queues/<queueId> \
  -H "Content-Type: application/json" \
  -d '{ "name": "PNS Daily 5558", "cycleExecution": false, "pauseWhenIdle": true, "idleThresholdSeconds": 30 }'
```

Or via the web-ui queue editor: toggle **Pause game when idle** and set the threshold, then save.

Config is read at run start, so **restart the queue** for the change to take effect on a currently
running queue.

## Verify

1. Start the queue with a next task more than the threshold away.
2. Within ~one poll interval the game is backed out to the device home screen.
3. Open the queue monitor: **current** shows *Idle Pause* with a resume time; **up next** shows the due
   sequence as the soonest item.
4. At (approximately) the scheduled time the game returns to the foreground and the sequence runs.
5. Confirm the execution log has **no** new entries for the pause itself (only the scheduled sequences).

## Rollout: retire the old fixed-wait pause

The "PNS Queue Pause 15m" template entry is superseded (its 15-min wait was killed by the 4-min
watchdog and never resumed/re-armed). Remove it from the `PNS Daily 5558` template and restart the
queue. Keep the sequence definition in the library (no delete).

## Behavior notes

- **Churn is expected**: with tasks every few minutes the game may background/foreground often; that is
  intended (goal: never idle-running for more than the threshold).
- **Earlier firings win**: a live/ad-hoc schedule or self-reschedule that becomes due sooner ends the
  pause early.
- **Non-fatal**: if backgrounding or foregrounding fails, scheduled tasks still run.
- **Stop is prompt**: stopping the queue during a pause takes effect within one poll interval; the game
  is left backgrounded (as any stop leaves the device where the last action left it).
- **Disabled queues**: unchanged behavior — game left as-is during gaps, no pause state shown.

## Part B — Retire the MCP server

The project's own MCP server (`src/mcp-server`, registered in `.mcp.json`) is removed completely. It was
a thin client over the REST API, so every capability remains reachable via `/api/*` and the web-ui.

### What to remove

```bash
# From the repo root:
git rm -r src/mcp-server        # entire component (source, dist, manifests, vendored deps)
git rm .mcp.json                # root MCP registration
# Then edit docs/architecture.md: drop the incidental "/ MCP start_session" phrase (~line 72).
```

### What NOT to touch

- `.github/agents/speckit.taskstoissues.agent.md` → references the **external** `github/github-mcp-server`
  (unrelated tooling MCP). Leave it.
- Prior specs (069/070/071) mentioning `src/mcp-server/src/tools/*` are immutable history — do not edit.
- `.claude/settings.local.json` local allow-rules mentioning `mcp-server` are developer-only/untracked;
  optional to clean, not required.

### Verify

1. `src/mcp-server/` and `.mcp.json` no longer exist.
2. Repo-wide search for `mcp-server` / `gamebot-mcp` returns only spec-history hits and the external
   GitHub-MCP agent file.
3. Full build + test gate is green: `dotnet build` + `dotnet test`, and web-ui `vite build` + `jest`.
4. REST API and web-ui behave exactly as before (the MCP only wrapped existing endpoints).
