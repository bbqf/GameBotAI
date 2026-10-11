# Quickstart: Measure the Build Time

Use absolute paths. Do not run other heavy programs during a measurement.

1. Test baseline (once, first). Run the full test suite and record the result. This run fills `bin` and `obj`, so it comes BEFORE every clean step.
2. Restore once: `dotnet restore C:\src\GameBot\GameBot.sln`.
3. Official time (3 runs). Before EACH run, do the clean step: delete `bin` and `obj` folders under `C:\src\GameBot\src` and `C:\src\GameBot\tests`. Then run the exact command, with no extra option:
   `Measure-Command { dotnet build C:\src\GameBot\GameBot.sln }` (Debug configuration).
   Record worst and median. Limit: under 4 minutes. Target: 3 minutes or less. Write down whether the build starts a web UI step (for example, "no web UI step in the default build").
4. Cause data (one extra run, clean state first):
   `dotnet build C:\src\GameBot\GameBot.sln /bl:C:\src\GameBot\baseline.binlog /p:ReportAnalyzer=true -clp:PerformanceSummary`
   Open the binlog in MSBuild Structured Log Viewer. Read: per-project time, per-target time, analyzer table, build count per project.
5. Incremental time: run the command of step 3 again with no change. Limit: under 30 seconds.
6. Per-project incremental check: change one source file in `GameBot.Domain`, build, and note which projects compile again. Undo the change. Do the same for one file in `GameBot.Service`. Only projects that depend on the changed project may compile again.
7. After the fix, `scripts/measure-build.ps1` does steps 2 to 5 and prints the result against the limit.
8. Guide check (SC-007): follow `docs/build-time.md` from the start and record the effort time, not counting the build run. Limit: under 10 minutes.

9. If a project split happened, do steps 5 and 6 again after the split. Do the final measurement (step 3) after all splits, with the kept settings and packages. Revert a setting or package that the final measurement does not confirm.
10. Last step: push the branch and wait for the CI runs. Make this step after the result record, the binlog cleanup, the doc links, and the status update. A later commit needs a new CI run.

The limits apply to the developer PC. Record the PC description with each result.

Do not commit binlog files.
