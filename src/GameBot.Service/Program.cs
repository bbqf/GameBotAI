using GameBot.Service.Security;
using GameBot.Service.Middleware;
using GameBot.Emulator.Session;
using GameBot.Service.Endpoints;
using System.Text.Json;
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
using GameBot.Service.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using GameBot.Service.Services.Detections;
using GameBot.Domain.Vision;
using Microsoft.AspNetCore.Http;
using GameBot.Service.Swagger;
using GameBot.Service;
using GameBot.Domain.Images;
using Microsoft.Extensions.FileProviders;

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
builder.Services.AddSwaggerDocs();
builder.Services.AddControllers();
// CORS for local web UI development (default: allow http://localhost:5173)
var corsOrigins = (builder.Configuration["Service:Cors:Origins"]
                  ?? Environment.GetEnvironmentVariable("GAMEBOT_CORS_ORIGINS")
                  ?? "http://localhost:5173")
                  .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options =>
{
  options.AddPolicy("WebUiCors", policy =>
  {
    if (corsOrigins.Length > 0)
      policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Capture-Id");
    else
      policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("X-Capture-Id");
  });
});
// Serialize enums as strings for API responses to match tests and readability
builder.Services.ConfigureHttpJsonOptions(options => {
  options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.Configure<GameBot.Emulator.Session.SessionOptions>(builder.Configuration.GetSection("Service:Sessions"));
builder.Services.Configure<GameBot.Service.Models.SessionCreationOptions>(options => {
  var envTimeout = Environment.GetEnvironmentVariable("GAMEBOT_SESSION_CREATE_TIMEOUT_SECONDS");
  var cfgTimeout = builder.Configuration["Service:Sessions:CreationTimeoutSeconds"];
  var raw = envTimeout ?? cfgTimeout;
  options.TimeoutSeconds = int.TryParse(raw, out var seconds) ? Math.Max(1, seconds) : 30;
});
builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddSingleton<ISessionContextCache, SessionContextCache>();
builder.Services.AddSingleton<ISessionService, SessionService>();
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
var imagesRoot = builder.Configuration["Service:Storage:Images"]
                  ?? Environment.GetEnvironmentVariable("GAMEBOT_IMAGES_DIR")
                  ?? Path.Combine(storageRoot, ImageStorageOptions.DefaultFolderName);
Directory.CreateDirectory(imagesRoot);
builder.Services.AddSingleton(new ImageStorageOptions(imagesRoot));
builder.Services.AddSingleton<GameBot.Domain.Triggers.Evaluators.IReferenceImageStore>(sp =>
  new GameBot.Domain.Triggers.Evaluators.ReferenceImageStore(sp.GetRequiredService<ImageStorageOptions>().Root));
builder.Services.AddSingleton<IImageRepository>(sp => new FileImageRepository(sp.GetRequiredService<ImageStorageOptions>().Root));
builder.Services.AddSingleton<IImageCaptureMetrics, ImageCaptureMetrics>();
builder.Services.AddSingleton<CaptureSessionStore>();
builder.Services.AddSingleton<ImageCropper>();
builder.Services.AddSingleton<IImageReferenceRepository>(sp => new TriggerImageReferenceRepository(sp.GetRequiredService<ITriggerRepository>()));
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
var installedWebUiRoot = Path.Combine(AppContext.BaseDirectory, "web-ui");
var hasInstalledWebUi = Directory.Exists(installedWebUiRoot) && File.Exists(Path.Combine(installedWebUiRoot, "index.html"));

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
app.UseSwaggerDocs();

// Global error handling
app.UseMiddleware<ErrorHandlingMiddleware>();

// Correlation IDs & scopes (added before auth so all logs include it)
app.UseCorrelationIds();

// CORS must run before auth to ensure preflight requests succeed
app.UseCors("WebUiCors");

if (hasInstalledWebUi) {
  var fileProvider = new PhysicalFileProvider(installedWebUiRoot);
  app.UseDefaultFiles(new DefaultFilesOptions {
    FileProvider = fileProvider,
    RequestPath = string.Empty
  });
  app.UseStaticFiles(new StaticFileOptions {
    FileProvider = fileProvider,
    RequestPath = string.Empty
  });
}

// Token auth for all non-health requests (Bearer <token>) if token configured
if (!string.IsNullOrWhiteSpace(authToken)) {
  app.Use(async (context, next) => {
    // Allow anonymous for health and swagger
    var path = context.Request.Path.Value ?? string.Empty;
    // Allow CORS preflight requests and public endpoints without auth
    if (HttpMethods.IsOptions(context.Request.Method) ||
        path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) {
      await next(context).ConfigureAwait(false);
      return;
    }

    await TokenAuthMiddleware.Invoke(context, next, authToken!).ConfigureAwait(false);
  });
}

// Health endpoint (anonymous)
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
  .WithName("Health")
  .ExcludeFromDescription();

if (!hasInstalledWebUi) {
  app.MapGet("/", () => Results.Ok(new { name = "GameBot Service", status = "ok" }))
    .WithName("Root")
    .ExcludeFromDescription();
}

// Sessions endpoints (protected if token set)
app.MapSessionEndpoints();

// Games endpoints (protected if token set)
app.MapGameEndpoints();
app.MapActionTypeEndpoints(storageRoot);
app.MapActionEndpoints();
// Commands endpoints (protected if token set)
app.MapCommandEndpoints();
// Triggers endpoints (re-added after refactor to support direct CRUD)
app.MapTriggerEndpoints();
// ADB diagnostics endpoints (protected if token set)
app.MapAdbEndpoints();
// Image references endpoints for image-match triggers
if (OperatingSystem.IsWindows()) {
  app.MapImageReferenceEndpoints();
  app.MapImageDetectionsEndpoints();
  app.MapEmulatorImageEndpoints();
}

// Metrics endpoints (protected if token set)
app.MapMetricsEndpoints();

// Config endpoints (protected if token set)
app.MapConfigEndpoints();
app.MapConfigLoggingEndpoints();
app.MapCoverageEndpoints();

// Sequences endpoints
var sequences = app.MapGroup(ApiRoutes.Sequences).WithTags("Sequences");

sequences.MapPost("", async (HttpRequest http, ISequenceRepository repo) =>
{
  using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body).ConfigureAwait(false);
  var root = doc.RootElement;
  // Authoring shape: { name: string, steps?: string[] }
  if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String && !root.TryGetProperty("blocks", out _))
  {
    var name = nameProp.GetString()!.Trim();
    var seq = new GameBot.Domain.Commands.CommandSequence { Id = string.Empty, Name = name, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
    if (root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
      var order = 0;
      var steps = new List<GameBot.Domain.Commands.SequenceStep>();
      foreach (var el in stepsProp.EnumerateArray())
      {
        if (el.ValueKind == System.Text.Json.JsonValueKind.String)
        {
          steps.Add(new GameBot.Domain.Commands.SequenceStep { Order = order++, CommandId = el.GetString()! });
        }
      }
      seq.SetSteps(steps);
    }
    var created = await repo.CreateAsync(seq).ConfigureAwait(false);
    return Results.Created($"{ApiRoutes.Sequences}/{created.Id}", new { id = created.Id, name = created.Name, steps = created.Steps.Select(s => s.CommandId).ToArray() });
  }
  // Fallback to domain shape
  var seqDomain = JsonSerializer.Deserialize<GameBot.Domain.Commands.CommandSequence>(root);
  if (seqDomain is null) return Results.BadRequest(new { message = "Invalid sequence payload" });
  var errors = ValidateSequence(seqDomain);
  if (errors.Count > 0) return Results.BadRequest(new { message = "Invalid sequence", errors });
  seqDomain.CreatedAt = DateTimeOffset.UtcNow;
  seqDomain.UpdatedAt = seqDomain.CreatedAt;
  var createdDomain = await repo.CreateAsync(seqDomain).ConfigureAwait(false);
  return Results.Created($"{ApiRoutes.Sequences}/{createdDomain.Id}", createdDomain);
}).Accepts<System.Text.Json.JsonElement>("application/json").WithName("CreateSequence");

sequences.MapGet("{id}", async (ISequenceRepository repo, string id) =>
{
  var found = await repo.GetAsync(id).ConfigureAwait(false);
  if (found is null) return Results.NotFound();
  return Results.Ok(new { id = found.Id, name = found.Name, steps = found.Steps.Select(s => s.CommandId).ToArray() });
}).WithName("GetSequence");

sequences.MapPut("{id}", async (HttpRequest http, ISequenceRepository repo, string id) =>
{
  var existing = await repo.GetAsync(id).ConfigureAwait(false);
  if (existing is null) return Results.NotFound();
  using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body).ConfigureAwait(false);
  var root = doc.RootElement;
  if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String)
  {
    var name = nameProp.GetString()!.Trim();
    if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
  }
  if (root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
  {
    var order = 0;
    var steps = new List<GameBot.Domain.Commands.SequenceStep>();
    foreach (var el in stepsProp.EnumerateArray())
    {
      if (el.ValueKind == System.Text.Json.JsonValueKind.String)
      {
        steps.Add(new GameBot.Domain.Commands.SequenceStep { Order = order++, CommandId = el.GetString()! });
      }
    }
    existing.SetSteps(steps);
  }
  existing.UpdatedAt = DateTimeOffset.UtcNow;
  var saved = await repo.UpdateAsync(existing).ConfigureAwait(false);
  return Results.Ok(new { id = saved.Id, name = saved.Name, steps = saved.Steps.Select(s => s.CommandId).ToArray() });
}).WithName("UpdateSequence");

sequences.MapGet("", async (ISequenceRepository repo) =>
{
  var list = await repo.ListAsync().ConfigureAwait(false);
  var resp = list.Select(s => new { id = s.Id, name = s.Name, steps = s.Steps.Select(x => x.CommandId).ToArray() });
  return Results.Ok(resp);
}).WithName("ListSequences");

sequences.MapDelete("{id}", async (ISequenceRepository repo, string id) =>
{
  var existing = await repo.GetAsync(id).ConfigureAwait(false);
  if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Sequence not found", hint = (string?)null } });
  var ok = await repo.DeleteAsync(id).ConfigureAwait(false);
  return ok ? Results.NoContent() : Results.NotFound(new { error = new { code = "not_found", message = "Sequence not found", hint = (string?)null } });
}).WithName("DeleteSequence");

sequences.MapPost("{id}/execute", async (
  GameBot.Domain.Services.SequenceRunner runner,
  TriggerEvaluationService evalSvc,
  string id,
  CancellationToken ct) =>
{
  // Minimal stub: delegate is a no-op; command execution integration will be added in later phases
  var res = await runner.ExecuteAsync(
    id,
    _ => Task.CompletedTask,
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
    },
    conditionEvaluator: (cond, token) =>
    {
      // Map Blocks.Condition to a transient Trigger and evaluate via TriggerEvaluationService
      if (string.Equals(cond.Source, "image", StringComparison.OrdinalIgnoreCase))
      {
        var region = cond.Region is null ? new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
                                         : new GameBot.Domain.Triggers.Region { X = cond.Region.X, Y = cond.Region.Y, Width = cond.Region.Width, Height = cond.Region.Height };
        var trig = new GameBot.Domain.Triggers.Trigger
        {
          Id = "inline-image",
          Type = GameBot.Domain.Triggers.TriggerType.ImageMatch,
          Enabled = true,
          Params = new GameBot.Domain.Triggers.ImageMatchParams
          {
            ReferenceImageId = cond.TargetId,
            Region = region,
            SimilarityThreshold = cond.ConfidenceThreshold ?? 0.85
          }
        };
        var r = evalSvc.Evaluate(trig, DateTimeOffset.UtcNow);
        return Task.FromResult(r.Status == GameBot.Domain.Triggers.TriggerStatus.Satisfied);
      }
      if (string.Equals(cond.Source, "text", StringComparison.OrdinalIgnoreCase))
      {
        var region = cond.Region is null ? new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
                                         : new GameBot.Domain.Triggers.Region { X = cond.Region.X, Y = cond.Region.Y, Width = cond.Region.Width, Height = cond.Region.Height };
        var mode = string.Equals(cond.Mode, "Absent", StringComparison.OrdinalIgnoreCase) ? "not-found" : "found";
        var trig = new GameBot.Domain.Triggers.Trigger
        {
          Id = "inline-text",
          Type = GameBot.Domain.Triggers.TriggerType.TextMatch,
          Enabled = true,
          Params = new GameBot.Domain.Triggers.TextMatchParams
          {
            Target = cond.TargetId,
            Region = region,
            ConfidenceThreshold = cond.ConfidenceThreshold ?? 0.80,
            Mode = mode,
            Language = cond.Language
          }
        };
        var r = evalSvc.Evaluate(trig, DateTimeOffset.UtcNow);
        return Task.FromResult(r.Status == GameBot.Domain.Triggers.TriggerStatus.Satisfied);
      }
      // Unsupported source
      return Task.FromResult(false);
    },
    ct: ct
  ).ConfigureAwait(false);
  return Results.Ok(res);
}).WithName("ExecuteSequence").WithTags("Sequences");

// Legacy guard rails: respond with guidance instead of serving old roots
MapLegacyGuard("/actions", ApiRoutes.Actions);
MapLegacyGuard("/commands", ApiRoutes.Commands);
MapLegacyGuard("/triggers", ApiRoutes.Triggers);
MapLegacyGuard("/games", ApiRoutes.Games);
MapLegacyGuard("/sessions", ApiRoutes.Sessions);
MapLegacyGuard("/images", ApiRoutes.Images);
MapLegacyGuard("/images/detect", ApiRoutes.ImageDetect);
MapLegacyGuard("/metrics", ApiRoutes.Metrics);
MapLegacyGuard("/config", ApiRoutes.Config);
MapLegacyGuard("/config/logging", ApiRoutes.ConfigLogging);
MapLegacyGuard("/adb", ApiRoutes.Adb);

app.MapControllers();

if (hasInstalledWebUi) {
  app.MapFallback(async context => {
    if (context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)) {
      context.Response.StatusCode = StatusCodes.Status404NotFound;
      return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(installedWebUiRoot, "index.html")).ConfigureAwait(false);
  }).ExcludeFromDescription();
}

app.Run();

void MapLegacyGuard(string legacyRoot, string canonicalRoot)
{
  app.MapMethods(legacyRoot, new[] { HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete },
    (HttpContext ctx) => Results.Json(
      new { error = new { code = "legacy_route", message = "Use the canonical API base path.", hint = canonicalRoot } },
      statusCode: StatusCodes.Status410Gone))
    .ExcludeFromDescription();

  app.MapMethods($"{legacyRoot}/{{*rest}}", new[] { HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete },
    (HttpContext ctx) => Results.Json(
      new { error = new { code = "legacy_route", message = "Use the canonical API base path.", hint = canonicalRoot } },
      statusCode: StatusCodes.Status410Gone))
    .ExcludeFromDescription();
}

static List<string> ValidateSequence(GameBot.Domain.Commands.CommandSequence seq)
{
  var errs = new List<string>();
  // Validate blocks if present
  if (seq.Blocks is { Count: > 0 })
  {
    foreach (var b in seq.Blocks)
    {
      ValidateBlock(b, errs, isTopLevel: true);
    }
  }
  return errs;
}

static void ValidateBlock(object blockObj, List<string> errs, bool isTopLevel)
{
  if (blockObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object)
  {
    string? type = null;
    if (je.TryGetProperty("type", out var tProp) && tProp.ValueKind == System.Text.Json.JsonValueKind.String)
    {
      type = tProp.GetString();
    }
    if (string.IsNullOrWhiteSpace(type))
    {
      errs.Add("Block missing required 'type'.");
      return;
    }
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "repeatCount", "repeatUntil", "while", "ifElse" };
    if (!allowed.Contains(type))
    {
      errs.Add($"Unsupported block type '{type}'.");
      return;
    }

    // Common: steps array for all but else-only
    if (je.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
      foreach (var item in stepsProp.EnumerateArray())
      {
        // item can be a Step (object without 'type') or a nested Block (object with 'type')
        if (item.ValueKind == System.Text.Json.JsonValueKind.Object && item.TryGetProperty("type", out var nestedType))
        {
          ValidateBlock(item, errs, isTopLevel: false);
        }
      }
    }

    if (type.Equals("ifElse", StringComparison.OrdinalIgnoreCase))
    {
      if (!je.TryGetProperty("condition", out var cond) || cond.ValueKind != System.Text.Json.JsonValueKind.Object)
      {
        errs.Add("ifElse block requires 'condition'.");
      }
      // Validate elseSteps only for ifElse
      if (je.TryGetProperty("elseSteps", out var elseProp) && elseProp.ValueKind == System.Text.Json.JsonValueKind.Array)
      {
        foreach (var item in elseProp.EnumerateArray())
        {
          if (item.ValueKind == System.Text.Json.JsonValueKind.Object && item.TryGetProperty("type", out var nestedType))
          {
            ValidateBlock(item, errs, isTopLevel: false);
          }
        }
      }
    }
    else if (type.Equals("repeatUntil", StringComparison.OrdinalIgnoreCase) || type.Equals("while", StringComparison.OrdinalIgnoreCase))
    {
      if (!je.TryGetProperty("condition", out var cond) || cond.ValueKind != System.Text.Json.JsonValueKind.Object)
      {
        errs.Add($"{type} block requires 'condition'.");
      }
      var hasTimeout = je.TryGetProperty("timeoutMs", out var to) && to.ValueKind == System.Text.Json.JsonValueKind.Number && to.GetInt32() >= 0;
      var hasMaxIter = je.TryGetProperty("maxIterations", out var mi) && mi.ValueKind == System.Text.Json.JsonValueKind.Number && mi.GetInt32() >= 1;
      if (!hasTimeout && !hasMaxIter)
      {
        errs.Add($"{type} block must set 'timeoutMs' or 'maxIterations'.");
      }
      if (je.TryGetProperty("cadenceMs", out var cadence) && cadence.ValueKind == System.Text.Json.JsonValueKind.Number)
      {
        var c = cadence.GetInt32();
        if (c < 50 || c > 5000)
        {
          errs.Add($"{type} cadenceMs out of bounds (50-5000): {c}.");
        }
      }
    }
    else if (type.Equals("repeatCount", StringComparison.OrdinalIgnoreCase))
    {
      if (!je.TryGetProperty("maxIterations", out var mi) || mi.ValueKind != System.Text.Json.JsonValueKind.Number || mi.GetInt32() < 0)
      {
        errs.Add("repeatCount requires non-negative 'maxIterations'.");
      }
      if (je.TryGetProperty("cadenceMs", out var cadence) && cadence.ValueKind == System.Text.Json.JsonValueKind.Number)
      {
        var c = cadence.GetInt32();
        if (c != 0 && (c < 50 || c > 5000))
        {
          errs.Add($"repeatCount cadenceMs must be 0 or within 50-5000: {c}.");
        }
      }
    }

    // If a non-ifElse block provides elseSteps, flag as error (T015)
    if (!type.Equals("ifElse", StringComparison.OrdinalIgnoreCase) && je.TryGetProperty("elseSteps", out var elseAny) && elseAny.ValueKind == System.Text.Json.JsonValueKind.Array)
    {
      errs.Add($"'elseSteps' is only valid for ifElse blocks, not '{type}'.");
    }
  }
}

// For WebApplicationFactory discovery in tests
internal partial class Program { }
