using GameBot.Service.Security;
using GameBot.Service.Middleware;
using GameBot.Emulator.Session;
using GameBot.Service.Endpoints;
using GameBot.Domain.Games;
using GameBot.Domain.Profiles;
using GameBot.Domain.Services;
using GameBot.Service.Hosted;
// Note: Avoid direct dependency on Microsoft.OpenApi.Models to keep CI restore simple
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Services
// Console logging with timestamps + scopes; allow env override for levels
builder.Logging.AddSimpleConsole(o =>
{
    o.IncludeScopes = true;
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff zzz ";
});
// Ensure our ADB category is visible at Debug if Default is higher
builder.Logging.AddFilter("GameBot.Emulator.Adb.AdbClient", LogLevel.Debug);
// Ensure trigger worker + text-match evaluator debug logs are visible
builder.Logging.AddFilter("GameBot.Service.Hosted.TriggerBackgroundWorker", LogLevel.Debug);
builder.Logging.AddFilter("GameBot.Domain.Profiles.Evaluators.TextMatchEvaluator", LogLevel.Debug);

builder.Services.AddEndpointsApiExplorer();
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

// Data storage configuration (env: GAMEBOT_DATA_DIR or config Service:Storage:Root)
var storageRoot = builder.Configuration["Service:Storage:Root"]
                  ?? Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")
                  ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(storageRoot);

builder.Services.AddSingleton<IGameRepository>(_ => new FileGameRepository(storageRoot));
builder.Services.AddSingleton<IProfileRepository>(_ => new FileProfileRepository(storageRoot));
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
                        using var ms = new MemoryStream(bytes);
                        return new System.Drawing.Bitmap(ms);
                    });
                }
                catch { }
            }
            return new GameBot.Domain.Profiles.Evaluators.SingleBitmapScreenSource(() => null);
        });
    }
    builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Profiles.Evaluators.ImageMatchEvaluator>();
    // Text match evaluator (OCR): prefer Tesseract if enabled and available, else env-backed stub
    var enableTess = Environment.GetEnvironmentVariable("GAMEBOT_TESSERACT_ENABLED");
    if (string.Equals(enableTess, "true", StringComparison.OrdinalIgnoreCase))
    {
        builder.Services.AddSingleton<GameBot.Domain.Profiles.Evaluators.ITextOcr, GameBot.Domain.Profiles.Evaluators.TesseractProcessOcr>();
    }
    else
    {
        builder.Services.AddSingleton<GameBot.Domain.Profiles.Evaluators.ITextOcr, GameBot.Domain.Profiles.Evaluators.EnvTextOcr>();
    }
    builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Profiles.Evaluators.TextMatchEvaluator>();
}
// Bind trigger worker options (env overrides supported via Configuration)
builder.Services.Configure<GameBot.Service.Hosted.TriggerWorkerOptions>(builder.Configuration.GetSection("Service:Triggers:Worker"));
builder.Services.AddSingleton<GameBot.Service.Hosted.ITriggerEvaluationMetrics, GameBot.Service.Hosted.TriggerEvaluationMetrics>();
builder.Services.AddHostedService<TriggerBackgroundWorker>();

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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

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
   .WithName("Health")
   .WithOpenApi();

// Placeholder root endpoint (protected if token set)
app.MapGet("/", () => Results.Ok(new { name = "GameBot Service", status = "ok" }))
   .WithName("Root")
   .WithOpenApi();

// Sessions endpoints (protected if token set)
app.MapSessionEndpoints();

// Games & Profiles endpoints (protected if token set)
app.MapGameEndpoints();
app.MapProfileEndpoints();
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

app.Run();

// For WebApplicationFactory discovery in tests
internal partial class Program { }
