using GameBot.Emulator.Adb;

namespace GameBot.Service.Endpoints;

internal static class AdbEndpoints
{
    private static readonly char[] LineSeparators = new[] { '\r', '\n' }; // CA1861: reuse array

    public static IEndpointRouteBuilder MapAdbEndpoints(this IEndpointRouteBuilder app)
    {
    app.MapGet("/adb/version", async (ILogger<AdbClient> logger) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.StatusCode(StatusCodes.Status501NotImplemented);
            }
            var useAdbEnv = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
            if (string.Equals(useAdbEnv, "false", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(new { version = "disabled" });
            }
            var adb = new AdbClient(logger);
            var (code, stdout, stderr) = await adb.ExecAsync("version").ConfigureAwait(false);
            return code == 0
                ? Results.Ok(new { version = stdout })
                : Results.Problem(title: "adb_error", detail: string.IsNullOrWhiteSpace(stderr) ? stdout : stderr, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).WithName("AdbVersion");

    app.MapGet("/adb/devices", async (ILogger<AdbClient> logger) =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.StatusCode(StatusCodes.Status501NotImplemented);
            }
            var useAdbEnv = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
            if (string.Equals(useAdbEnv, "false", StringComparison.OrdinalIgnoreCase))
            {
                // When ADB is disabled (tests/CI), return an empty array to avoid spawning adb server and potential hangs
                return Results.Ok(Array.Empty<object>());
            }

            var adb = new AdbClient(logger);
            var (code, stdout, stderr) = await adb.ExecAsync("devices -l").ConfigureAwait(false);
            if (code != 0)
            {
                return Results.Problem(title: "adb_error", detail: string.IsNullOrWhiteSpace(stderr) ? stdout : stderr, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var devices = ParseDevices(stdout);
            // Return devices as a top-level array (tests expect an array, not an object wrapper)
            return Results.Ok(devices);
        }).WithName("AdbDevices");

        return app;
    }

    private static List<object> ParseDevices(string output)
    {
        var list = new List<object>();
        if (string.IsNullOrWhiteSpace(output)) return list;

        var lines = output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            var serial = parts[0];
            var state = parts.Length > 1 ? parts[1] : string.Empty;
            var extra = string.Join(' ', parts.Skip(2));
            list.Add(new { serial, state, info = extra });
        }
        return list;
    }
}
