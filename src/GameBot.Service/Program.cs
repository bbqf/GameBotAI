using GameBot.Service.Security;
using GameBot.Service.Middleware;
using GameBot.Emulator.Session;
using GameBot.Service.Endpoints;
using GameBot.Domain.Games;
using GameBot.Domain.Profiles;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Service.Hosted;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Services
// Console logging with timestamps + scopes; ensure no duplicate console providers
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.IncludeScopes = true;
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff zzz ";
});
// Reduce noisy/double Kestrel connection logs
builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel.Connections", LogLevel.Warning);
// Ensure our ADB category is visible at Debug if Default is higher
builder.Logging.AddFilter("GameBot.Emulator.Adb.AdbClient", LogLevel.Debug);
// Ensure text-match evaluator debug logs are visible
builder.Logging.AddFilter("GameBot.Domain.Profiles.Evaluators.TextMatchEvaluator", LogLevel.Debug);
builder.Logging.AddFilter("GameBot.Domain.Profiles.Evaluators.TextMatchEvaluator", LogLevel.Debug);

builder.Services.AddEndpointsApiExplorer();
// Explicitly register v1 document so tests can fetch /swagger/v1/swagger.json across environments (CI may not be Development)
builder.Services.AddSwaggerGen();
// Serialize enums as strings for API responses to match tests and readability
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.Configure<GameBot.Emulator.Session.SessionOptions>(builder.Configuration.GetSection("Service:Sessions"));
builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddTransient<ErrorHandlingMiddleware>();
builder.Services.AddTransient<CorrelationIdMiddleware>();
builder.Services.AddSingleton<IProfileExecutor, ProfileExecutor>();
builder.Services.AddSingleton<GameBot.Service.Services.ICommandExecutor, GameBot.Service.Services.CommandExecutor>();

// Data storage configuration (env: GAMEBOT_DATA_DIR or config Service:Storage:Root)
var storageRoot = builder.Configuration["Service:Storage:Root"]
                  ?? Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")
                  ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(storageRoot);

builder.Services.AddSingleton<IGameRepository>(_ => new FileGameRepository(storageRoot));
builder.Services.AddSingleton<IProfileRepository>(_ => new FileProfileRepository(storageRoot));
// New repositories for Actions and Commands (001-action-command-refactor)
builder.Services.AddSingleton<IActionRepository>(_ => new FileActionRepository(storageRoot));
builder.Services.AddSingleton<ICommandRepository>(_ => new FileCommandRepository(storageRoot));
// Config snapshot service (for /config endpoints and persisted snapshot generation)
builder.Services.AddSingleton<GameBot.Service.Services.IConfigApplier, GameBot.Service.Services.ConfigApplier>();
builder.Services.AddSingleton<GameBot.Service.Services.IConfigSnapshotService>(sp => new GameBot.Service.Services.ConfigSnapshotService(storageRoot, sp.GetRequiredService<GameBot.Service.Services.IConfigApplier>()));
builder.Services.AddSingleton<TriggerEvaluationService>();
builder.Services.AddSingleton<ITriggerEvaluationCoordinator, TriggerEvaluationCoordinator>();
builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Profiles.Evaluators.DelayTriggerEvaluator>();
builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Profiles.Evaluators.ScheduleTriggerEvaluator>();
// Image match evaluator dependencies (in-memory store + screen source placeholder)
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<GameBot.Domain.Profiles.Evaluators.IReferenceImageStore, GameBot.Domain.Profiles.Evaluators.MemoryReferenceImageStore>();
    var useAdbEnv = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    var useAdb = !string.Equals(useAdbEnv, "false", StringComparison.OrdinalIgnoreCase);
    if (useAdb)
    {
        // ADB-backed dynamic screen source via sessions
        builder.Services.AddSingleton<GameBot.Domain.Profiles.Evaluators.IScreenSource, GameBot.Emulator.Session.AdbScreenSource>();
    }
    else
    {
        // Test/stub mode: optional fixed bitmap via env variable
        builder.Services.AddSingleton<GameBot.Domain.Profiles.Evaluators.IScreenSource>(_ =>
        {
            var b64 = Environment.GetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64");
            if (!string.IsNullOrWhiteSpace(b64))
            {
                try
                {
                    var data = b64;
                    var comma = data.IndexOf(',', System.StringComparison.Ordinal);
                    if (comma >= 0) data = data[(comma + 1)..];
                    var bytes = Convert.FromBase64String(data);
                    return new GameBot.Domain.Profiles.Evaluators.SingleBitmapScreenSource(() =>
                    {
                        // Important: detach bitmap from stream to avoid disposed-stream issues
                        using var ms = new MemoryStream(bytes, writable: false);
                        using var tmp = new System.Drawing.Bitmap(ms);
                        return new System.Drawing.Bitmap(tmp);
                    });
                }
                catch { }
            }
            return new GameBot.Domain.Profiles.Evaluators.SingleBitmapScreenSource(() => null);
        });
    }
    builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Profiles.Evaluators.ImageMatchEvaluator>();
    // Text match evaluator (OCR): dynamic backend selection based on refreshed configuration
    builder.Services.AddSingleton<GameBot.Domain.Profiles.Evaluators.ITextOcr, GameBot.Service.Services.DynamicTextOcr>();
    builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Profiles.Evaluators.TextMatchEvaluator>();
}
    // Reduce chatty HTTP logs by default; allow dynamic override via GAMEBOT_HTTP_LOG_LEVEL_MINIMUM applied on refresh
    var httpMinLevelEnv = Environment.GetEnvironmentVariable("GAMEBOT_HTTP_LOG_LEVEL_MINIMUM");
    GameBot.Service.Services.DynamicLogFilters.HttpMinLevel = httpMinLevelEnv?.Trim().ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "information" => LogLevel.Information,
        "info" => LogLevel.Information,
        "warning" => LogLevel.Warning,
        "warn" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "critical" => LogLevel.Critical,
        _ => LogLevel.Warning
    };
    builder.Logging.AddFilter((category, provider, level) =>
    {
        if (!string.IsNullOrEmpty(category) && GameBot.Service.Services.DynamicLogFilters.IsHttpCategory(category))
        {
            return level >= GameBot.Service.Services.DynamicLogFilters.HttpMinLevel;
        }
        return true;
    });
// Expose trigger evaluation metrics (no background evaluation in refactor)
builder.Services.AddSingleton<GameBot.Service.Hosted.ITriggerEvaluationMetrics, GameBot.Service.Hosted.TriggerEvaluationMetrics>();
builder.Services.AddHostedService<GameBot.Service.Hosted.ConfigSnapshotStartupInitializer>();

// Configuration binding for auth token (env: GAMEBOT_AUTH_TOKEN)
var authToken = builder.Configuration["Service:Auth:Token"]
                 ?? Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");

// In CI/tests (or when explicitly requested), avoid fixed ports to prevent socket bind conflicts
var dynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
if (string.Equals(dynPort, "true", StringComparison.OrdinalIgnoreCase))
{
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

var app = builder.Build();

// Enable Swagger in all environments for contract tests & debugging (locked down by token auth for non-health if configured)
app.UseSwagger();
app.UseSwaggerUI();

// Global error handling
app.UseMiddleware<ErrorHandlingMiddleware>();

// Correlation IDs & scopes (added before auth so all logs include it)
app.UseCorrelationIds();

// Token auth for all non-health requests (Bearer <token>) if token configured
if (!string.IsNullOrWhiteSpace(authToken))
{
    app.Use(async (context, next) =>
    {
        // Allow anonymous for health and swagger
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        await TokenAuthMiddleware.Invoke(context, next, authToken!).ConfigureAwait(false);
    });
}

// Health endpoint (anonymous)
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health");

// Placeholder root endpoint (protected if token set)
app.MapGet("/", () => Results.Ok(new { name = "GameBot Service", status = "ok" }))
   .WithName("Root");

// Sessions endpoints (protected if token set)
app.MapSessionEndpoints();

// Games & Profiles endpoints (protected if token set)
app.MapGameEndpoints();
app.MapProfileEndpoints();
// Actions & Commands endpoints (protected if token set)
app.MapActionEndpoints();
app.MapCommandEndpoints();
// ADB diagnostics endpoints (protected if token set)
app.MapAdbEndpoints();
// Triggers endpoints (protected if token set)
app.MapTriggersEndpoints();
// Image references endpoints for image-match triggers
if (OperatingSystem.IsWindows())
{
    app.MapImageReferenceEndpoints();
}

// Metrics endpoints (protected if token set)
app.MapMetricsEndpoints();

// Config endpoints (protected if token set)
app.MapConfigEndpoints();

app.Run();

// For WebApplicationFactory discovery in tests
internal partial class Program { }
