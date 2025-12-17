using GameBot.Service.Security;
using GameBot.Service.Middleware;
using GameBot.Emulator.Session;
using GameBot.Service.Endpoints;
using GameBot.Domain.Games;
using GameBot.Domain.Triggers;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Logging;
using GameBot.Domain.Services.Logging;
using GameBot.Service.Hosted;
using GameBot.Service.Logging;
using GameBot.Service.Services.Ocr;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using GameBot.Service.Services.Detections;
using GameBot.Domain.Vision;

var builder = WebApplication.CreateBuilder(args);

var loggingComponentCatalog = new[]
{
  "GameBot.Service",
  "GameBot.Domain.Triggers",
  "GameBot.Domain.Triggers.Evaluators.TextMatchEvaluator",
  "GameBot.Domain.Actions",
  "GameBot.Domain.Commands",
  "GameBot.Emulator.Adb.AdbClient",
  "Microsoft.AspNetCore",
  "Microsoft.AspNetCore.Server.Kestrel.Connections"
};

var loggingGate = new LoggingPolicyGate();

// Services
// Console logging with timestamps + scopes; ensure no duplicate console providers
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Trace);
builder.Services.Configure<LoggerFilterOptions>(options =>
{
  options.Rules.Clear();
  options.MinLevel = LogLevel.Trace;
});
builder.Logging.AddSimpleConsole(o => {
  o.IncludeScopes = true;
  o.SingleLine = true;
  o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff zzz ";
});
builder.Logging.AddRuntimeLoggingGate(loggingGate);

builder.Services.AddEndpointsApiExplorer();
// Explicitly register v1 document so tests can fetch /swagger/v1/swagger.json across environments (CI may not be Development)
builder.Services.AddSwaggerGen();
// Serialize enums as strings for API responses to match tests and readability
builder.Services.ConfigureHttpJsonOptions(options => {
  options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.Configure<GameBot.Emulator.Session.SessionOptions>(builder.Configuration.GetSection("Service:Sessions"));
builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddTransient<ErrorHandlingMiddleware>();
builder.Services.AddTransient<CorrelationIdMiddleware>();
builder.Services.AddSingleton<GameBot.Service.Services.ICommandExecutor, GameBot.Service.Services.CommandExecutor>();

// Data storage configuration (env: GAMEBOT_DATA_DIR or config Service:Storage:Root)
var storageRoot = builder.Configuration["Service:Storage:Root"]
                  ?? Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")
                  ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(storageRoot);

builder.Services.AddSingleton<IGameRepository>(_ => new FileGameRepository(storageRoot));
builder.Services.AddSingleton<ITriggerRepository>(_ => new FileTriggerRepository(storageRoot));
// New repositories for Actions and Commands (001-action-command-refactor)
builder.Services.AddSingleton<IActionRepository>(_ => new FileActionRepository(storageRoot));
builder.Services.AddSingleton<ICommandRepository>(_ => new FileCommandRepository(storageRoot));
builder.Services.AddSingleton<ISequenceRepository>(_ => new FileSequenceRepository(storageRoot));
builder.Services.AddSingleton<GameBot.Domain.Services.SequenceRunner>();
builder.Services.AddSingleton(loggingGate);
builder.Services.AddSingleton<ILoggingPolicyApplier>(sp => sp.GetRequiredService<LoggingPolicyGate>());
builder.Services.AddSingleton<ILoggingPolicyRepository>(sp =>
{
  var logger = sp.GetRequiredService<ILogger<LoggingPolicyRepository>>();
  return new LoggingPolicyRepository(storageRoot, () => LoggingPolicySnapshot.CreateDefault(loggingComponentCatalog, LogLevel.Warning, "system-seed"), logger);
});
builder.Services.AddSingleton(sp =>
{
  var repo = sp.GetRequiredService<ILoggingPolicyRepository>();
  var logger = sp.GetRequiredService<ILogger<RuntimeLoggingPolicyService>>();
  var applier = sp.GetRequiredService<ILoggingPolicyApplier>();
  return new RuntimeLoggingPolicyService(repo, loggingComponentCatalog, logger, applier);
});
builder.Services.AddSingleton<IRuntimeLoggingPolicyService>(sp => sp.GetRequiredService<RuntimeLoggingPolicyService>());
// Config snapshot service (for /config endpoints and persisted snapshot generation)
builder.Services.AddSingleton<GameBot.Service.Services.IConfigApplier, GameBot.Service.Services.ConfigApplier>();
builder.Services.AddSingleton<GameBot.Service.Services.IConfigSnapshotService>(sp => new GameBot.Service.Services.ConfigSnapshotService(storageRoot, sp.GetRequiredService<GameBot.Service.Services.IConfigApplier>()));
builder.Services.AddSingleton<TriggerEvaluationService>();
builder.Services.AddSingleton<ITriggerEvaluationCoordinator, TriggerEvaluationCoordinator>();
builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Triggers.Evaluators.DelayTriggerEvaluator>();
builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Triggers.Evaluators.ScheduleTriggerEvaluator>();
builder.Services.AddSingleton<GameBot.Domain.Triggers.Evaluators.ITesseractInvocationLogger, TesseractInvocationLogger>();
builder.Services.AddSingleton<ICoverageSummaryService>(sp => new CoverageSummaryService(storageRoot, sp.GetRequiredService<ILogger<CoverageSummaryService>>()));
// Image match evaluator dependencies (disk-backed store + screen source placeholder)
var imagesRoot = Path.Combine(storageRoot, "images");
Directory.CreateDirectory(imagesRoot);
builder.Services.AddSingleton<GameBot.Domain.Triggers.Evaluators.IReferenceImageStore>(_ => new GameBot.Domain.Triggers.Evaluators.ReferenceImageStore(imagesRoot));
if (OperatingSystem.IsWindows()) {
  var useAdbEnv = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
  var useAdb = !string.Equals(useAdbEnv, "false", StringComparison.OrdinalIgnoreCase);
  if (useAdb) {
    // ADB-backed dynamic screen source via sessions
    builder.Services.AddSingleton<GameBot.Domain.Triggers.Evaluators.IScreenSource, GameBot.Emulator.Session.AdbScreenSource>();
  }
  else {
    // Test/stub mode: optional fixed bitmap via env variable
    builder.Services.AddSingleton<GameBot.Domain.Triggers.Evaluators.IScreenSource>(_ => {
      var b64 = Environment.GetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64");
      if (!string.IsNullOrWhiteSpace(b64)) {
        try {
          var data = b64;
          var comma = data.IndexOf(',', System.StringComparison.Ordinal);
          if (comma >= 0) data = data[(comma + 1)..];
          var bytes = Convert.FromBase64String(data);
          return new GameBot.Domain.Triggers.Evaluators.SingleBitmapScreenSource(() => {
            // Important: detach bitmap from stream to avoid disposed-stream issues
            using var ms = new MemoryStream(bytes, writable: false);
            using var tmp = new System.Drawing.Bitmap(ms);
            return new System.Drawing.Bitmap(tmp);
          });
        }
        catch { }
      }
      return new GameBot.Domain.Triggers.Evaluators.SingleBitmapScreenSource(() => null);
    });
  }
  builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Triggers.Evaluators.ImageMatchEvaluator>();
  // Text match evaluator (OCR): dynamic backend selection based on refreshed configuration
  builder.Services.AddSingleton<GameBot.Domain.Triggers.Evaluators.ITextOcr, GameBot.Service.Services.DynamicTextOcr>();
  builder.Services.AddSingleton<ITriggerEvaluator, GameBot.Domain.Triggers.Evaluators.TextMatchEvaluator>();
}
// Register template matcher (no endpoint yet; foundational only in Phase 2)
builder.Services.AddSingleton<GameBot.Domain.Vision.ITemplateMatcher, GameBot.Domain.Vision.TemplateMatcher>();
// Reduce chatty HTTP logs by default; allow dynamic override via GAMEBOT_HTTP_LOG_LEVEL_MINIMUM applied on refresh
var httpMinLevelEnv = Environment.GetEnvironmentVariable("GAMEBOT_HTTP_LOG_LEVEL_MINIMUM");
GameBot.Service.Services.DynamicLogFilters.HttpMinLevel = httpMinLevelEnv?.Trim().ToLowerInvariant() switch {
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
// Expose trigger evaluation metrics (no background evaluation in refactor)
builder.Services.AddSingleton<GameBot.Service.Hosted.ITriggerEvaluationMetrics, GameBot.Service.Hosted.TriggerEvaluationMetrics>();
builder.Services.AddHostedService<GameBot.Service.Hosted.ConfigSnapshotStartupInitializer>();
builder.Services.AddHostedService<LoggingPolicyStartupInitializer>();

// Bind detection options (threshold, max results, timeout, overlap)
builder.Services.Configure<DetectionOptions>(builder.Configuration.GetSection(DetectionOptions.SectionName));

// Configuration binding for auth token (env: GAMEBOT_AUTH_TOKEN)
var authToken = builder.Configuration["Service:Auth:Token"]
                 ?? Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");

// In CI/tests (or when explicitly requested), avoid fixed ports to prevent socket bind conflicts
var dynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
if (string.Equals(dynPort, "true", StringComparison.OrdinalIgnoreCase)) {
  builder.WebHost.UseUrls("http://127.0.0.1:0");
}

var app = builder.Build();

// Log basic runtime and OpenCV information at startup for diagnostics
{
  var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(GameBot.Service.Logging.DetectionsLogging.Category);
  var is64 = Environment.Is64BitProcess;
  var arch = RuntimeInformation.ProcessArchitecture.ToString();
  var buildInfo = CvRuntime.GetOpenCvBuildInformation();
  string firstLine;
  if (string.IsNullOrEmpty(buildInfo))
  {
    firstLine = "unavailable";
  }
  else
  {
    var span = buildInfo.AsSpan();
    var idx = span.IndexOfAny('\r', '\n');
    firstLine = idx >= 0 ? new string(span.Slice(0, idx)) : buildInfo;
  }
  logger.LogDetectionRuntimeInfo(is64, arch, firstLine);
}

// Enable Swagger in all environments for contract tests & debugging (locked down by token auth for non-health if configured)
app.UseSwagger();
app.UseSwaggerUI();

// Global error handling
app.UseMiddleware<ErrorHandlingMiddleware>();

// Correlation IDs & scopes (added before auth so all logs include it)
app.UseCorrelationIds();

// Token auth for all non-health requests (Bearer <token>) if token configured
if (!string.IsNullOrWhiteSpace(authToken)) {
  app.Use(async (context, next) => {
    // Allow anonymous for health and swagger
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) {
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

// Games endpoints (protected if token set)
app.MapGameEndpoints();
app.MapActionEndpoints();
// Actions & Commands endpoints (protected if token set)
app.MapCommandEndpoints();
// Triggers endpoints (re-added after refactor to support direct CRUD)
app.MapTriggerEndpoints();
// ADB diagnostics endpoints (protected if token set)
app.MapAdbEndpoints();
// Standalone triggers CRUD
// Image references endpoints for image-match triggers
if (OperatingSystem.IsWindows()) {
  app.MapImageReferenceEndpoints();
  app.MapImageDetectionsEndpoints();
}

// Metrics endpoints (protected if token set)
app.MapMetricsEndpoints();

// Config endpoints (protected if token set)
app.MapConfigEndpoints();
app.MapConfigLoggingEndpoints();
app.MapCoverageEndpoints();

// Sequences endpoints (Phase 3 US1 minimal stubs)
app.MapPost("/api/sequences", async (ISequenceRepository repo, CommandSequence seq) =>
{
  seq.CreatedAt = DateTimeOffset.UtcNow;
  seq.UpdatedAt = seq.CreatedAt;
  var created = await repo.CreateAsync(seq).ConfigureAwait(false);
  return Results.Created($"/api/sequences/{created.Id}", created);
}).WithName("CreateSequence");

app.MapGet("/api/sequences/{id}", async (ISequenceRepository repo, string id) =>
{
  var found = await repo.GetAsync(id).ConfigureAwait(false);
  return found is null ? Results.NotFound() : Results.Ok(found);
}).WithName("GetSequence");

app.MapPost("/api/sequences/{id}/execute", async (
  GameBot.Domain.Services.SequenceRunner runner,
  string id,
  CancellationToken ct) =>
{
  // Minimal stub: delegate is a no-op; command execution integration will be added in later phases
  var res = await runner.ExecuteAsync(
    id,
    _ => Task.CompletedTask,
    ct,
    gateEvaluator: (step, token) =>
    {
      // Temporary evaluator for integration tests:
      // TargetId "always" => gate passes; "never" => gate fails
      if (step.Gate == null) return Task.FromResult(true);
      var tid = step.Gate.TargetId ?? string.Empty;
      if (string.Equals(tid, "always", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(true);
      if (string.Equals(tid, "never", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(false);
      // Default: pass
      return Task.FromResult(true);
    }
  ).ConfigureAwait(false);
  return Results.Ok(res);
}).WithName("ExecuteSequence");

app.Run();

// For WebApplicationFactory discovery in tests
internal partial class Program { }
