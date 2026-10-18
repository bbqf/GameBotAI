# Quickstart: Fix gamepack install file replace failure

1. Build: `dotnet build C:\src\GameBot\GameBot.sln`.
2. Run the new tests: `dotnet test C:\src\GameBot\GameBot.sln --filter "FullyQualifiedName~AtomicFile|FullyQualifiedName~ImageRepository|FullyQualifiedName~GamepackApplier|FullyQualifiedName~GamepackInstall"`. The root cause test must fail on the old code and pass on the new code.
3. Full-scale check: the 259-object test installs a pack on top of the same 259 objects. It must pass for sequences, commands, images and templates.
4. Manual check on Windows: start the service, install a pack with take-over twice. Hold one image file open with `FileShare.Read` for 1 second during the install. The install must succeed after the lock ends. Hold it for 10 seconds: the install must return 500 with the text `Cannot replace file '<full path>' for image '<id>': ...`.
5. Force a restore failure with the write gate in a test. The response must hold `failedRestoreKeys` in the format `<kind>:<id> (<full file path>)`.
