using GameBot.Emulator.Adb;

namespace GameBot.Service.Endpoints;

internal static class AdbEndpoints
{
    private static readonly char[] LineSeparators = new[] { '\r', '\n' }; // CA1861: reuse array

    public static IEndpointRouteBuilder MapAdbEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/adb/version", async () =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.StatusCode(StatusCodes.Status501NotImplemented);
            }
            var adb = new AdbClient();
            var (code, stdout, stderr) = await adb.ExecAsync("version").ConfigureAwait(false);
            return code == 0
                ? Results.Ok(new { version = stdout })
                : Results.Problem(title: "adb_error", detail: string.IsNullOrWhiteSpace(stderr) ? stdout : stderr, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).WithName("AdbVersion").WithOpenApi();

        app.MapGet("/adb/devices", async () =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return Results.StatusCode(StatusCodes.Status501NotImplemented);
            }
            var adb = new AdbClient();
            var (code, stdout, stderr) = await adb.ExecAsync("devices -l").ConfigureAwait(false);
            if (code != 0)
            {
                return Results.Problem(title: "adb_error", detail: string.IsNullOrWhiteSpace(stderr) ? stdout : stderr, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var devices = ParseDevices(stdout);
            return Results.Ok(new { devices });
        }).WithName("AdbDevices").WithOpenApi();

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
