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
using Microsoft.Win32;
using GameBot.Domain.Versioning;
using GameBot.Service.Contracts.Sequences;
using Swashbuckle.AspNetCore.SwaggerGen;
using GameBot.Service.Services.Conditions;

var builder = WebApplication.CreateBuilder(args);
var flowRequestJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

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
builder.Services.Configure<LoggerFilterOptions>(options => {
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
builder.Services.AddSwaggerGen(options => {
  options.DocumentFilter<ConditionalFlowSchemaDocumentFilter>();
});
builder.Services.AddControllers();
// CORS for local web UI development (default: allow http://localhost:5173)
var corsOrigins = (builder.Configuration["Service:Cors:Origins"]
                  ?? Environment.GetEnvironmentVariable("GAMEBOT_CORS_ORIGINS")
                  ?? "http://localhost:5173")
                  .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => {
  options.AddPolicy("WebUiCors", policy => {
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
builder.Services.AddSingleton<IExecutionLogRepository>(_ => new FileExecutionLogRepository(storageRoot));
builder.Services.AddSingleton<IExecutionLogRetentionPolicyRepository>(_ => new ExecutionLogRetentionPolicyRepository(storageRoot));
builder.Services.AddSingleton<GameBot.Service.Services.ExecutionLog.IExecutionLogService, GameBot.Service.Services.ExecutionLog.ExecutionLogService>();
builder.Services.AddSingleton<SemanticVersionComparer>();
builder.Services.AddSingleton<VersionSourceLoader>();
builder.Services.AddSingleton<VersionResolutionService>();
builder.Services.AddSingleton<ISequenceFlowValidator, SequenceFlowValidator>();
builder.Services.AddSingleton<CycleIterationLimiter>();
builder.Services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
builder.Services.AddSingleton<ICommandOutcomeConditionAdapter, CommandOutcomeConditionAdapter>();
builder.Services.AddSingleton<IImageDetectionConditionAdapter, ImageDetectionConditionAdapter>();
builder.Services.AddSingleton<GameBot.Domain.Services.SequenceRunner>();
builder.Services.AddSingleton(loggingGate);
builder.Services.AddSingleton<ILoggingPolicyApplier>(sp => sp.GetRequiredService<LoggingPolicyGate>());
builder.Services.AddSingleton<ILoggingPolicyRepository>(sp => {
  var logger = sp.GetRequiredService<ILogger<LoggingPolicyRepository>>();
  return new LoggingPolicyRepository(storageRoot, () => LoggingPolicySnapshot.CreateDefault(loggingComponentCatalog, LogLevel.Warning, "system-seed"), logger);
});
builder.Services.AddSingleton(sp => {
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
builder.Services.AddHostedService<GameBot.Service.Hosted.ExecutionLogRetentionCleanupService>();

// Bind detection options (threshold, max results, timeout, overlap)
builder.Services.Configure<DetectionOptions>(builder.Configuration.GetSection(DetectionOptions.SectionName));

// Configuration binding for auth token (env: GAMEBOT_AUTH_TOKEN)
var authToken = builder.Configuration["Service:Auth:Token"]
                 ?? Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");

// In CI/tests (or when explicitly requested), avoid fixed ports to prevent socket bind conflicts
var dynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
var explicitUrlsEnv = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
var hasUrlsArgument = Environment.GetCommandLineArgs().Any(arg =>
  arg.Equals("--urls", StringComparison.OrdinalIgnoreCase) ||
  arg.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase));

if (string.Equals(dynPort, "true", StringComparison.OrdinalIgnoreCase)) {
  builder.WebHost.UseUrls("http://127.0.0.1:0");
}
else if (string.IsNullOrWhiteSpace(explicitUrlsEnv) && !hasUrlsArgument) {
  var bindHost = builder.Configuration["Service:Network:BindHost"]
                 ?? Environment.GetEnvironmentVariable("GAMEBOT_BIND_HOST")
                 ?? ReadInstallerNetworkValue("BindHost")
                 ?? "127.0.0.1";

  var portRaw = builder.Configuration["Service:Network:Port"]
                ?? Environment.GetEnvironmentVariable("GAMEBOT_PORT")
                ?? ReadInstallerNetworkValue("Port")
                ?? "8080";
  if (!int.TryParse(portRaw, out var port) || port < 1 || port > 65535) {
    port = 8080;
  }

  builder.WebHost.UseUrls($"http://{bindHost}:{port}");
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
  if (string.IsNullOrEmpty(buildInfo)) {
    firstLine = "unavailable";
  }
  else {
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
app.MapExecutionLogEndpoints();

app.MapPost("/versioning/resolve", (GameBot.Service.Models.VersionResolveRequestModel request, VersionSourceLoader loader) => {
  if (!Enum.TryParse<BuildContext>(request.BuildContext, ignoreCase: true, out var buildContext)) {
    return Results.BadRequest(new { code = "invalid_build_context", message = "buildContext must be one of: ci, local." });
  }

  var versioningDirectory = TryFindVersioningDirectory();
  VersionOverride? fileOverride = null;
  ReleaseLineMarker? fileMarker = null;
  CiBuildCounter? fileCounter = null;
  var notes = new List<string>();

  if (!string.IsNullOrWhiteSpace(versioningDirectory)) {
    try {
      fileOverride = loader.LoadOverrideFromDirectory(versioningDirectory);
      fileMarker = loader.LoadReleaseLineMarkerFromDirectory(versioningDirectory);
      fileCounter = loader.LoadCiBuildCounterFromDirectory(versioningDirectory);
      notes.Add("source:versioning-files");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
      return Results.BadRequest(new { code = "invalid_versioning_sources", message = ex.Message });
    }
  }
  else {
    notes.Add("source:request-only");
  }

  var requestOverride = request.Override;
  var effectiveOverride = new VersionOverride {
    Major = requestOverride?.Major ?? request.Major ?? fileOverride?.Major,
    Minor = requestOverride?.Minor ?? request.Minor ?? fileOverride?.Minor,
    Patch = requestOverride?.Patch ?? request.Patch ?? fileOverride?.Patch
  };

  if (effectiveOverride.Major is not null || effectiveOverride.Minor is not null || effectiveOverride.Patch is not null) {
    notes.Add("source:override");
  }

  var requestMarker = request.ReleaseLineMarker;
  var previousSequence = fileMarker?.Sequence;
  var requestedSequence = requestMarker?.Sequence ?? request.ReleaseLineSequence ?? fileMarker?.Sequence;
  var transitionDetected = VersionResolutionService.HasReleaseLineTransition(previousSequence, requestedSequence);
  if (transitionDetected) {
    notes.Add("source:release-line-transition");
  }

  var requestCounter = request.CiBuildCounter?.LastBuild ?? request.LastCiBuild;
  var effectiveLastBuild = requestCounter ?? fileCounter?.LastBuild ?? 0;
  if (requestCounter.HasValue) {
    notes.Add("source:counter-request");
  }
  else if (fileCounter is not null) {
    notes.Add("source:counter-file");
  }

  var resolutionInput = new VersionResolutionInput {
    BaselineVersion = new SemanticVersion(1, 0, 0, Math.Max(0, effectiveLastBuild)),
    Override = effectiveOverride,
    ReleaseLineTransitionDetected = transitionDetected,
    PreviousReleaseLineSequence = previousSequence,
    CurrentReleaseLineSequence = requestedSequence,
    CiBuildCounter = new CiBuildCounter {
      LastBuild = Math.Max(0, effectiveLastBuild),
      UpdatedAtUtc = DateTimeOffset.UtcNow,
      UpdatedBy = string.Equals(buildContext, BuildContext.Ci) ? "ci" : "local"
    },
    Context = buildContext
  };

  var resolved = VersionResolutionService.Resolve(resolutionInput);
  var responseNotes = resolved.Notes.Concat(notes).Distinct(StringComparer.Ordinal).ToArray();

  return Results.Ok(new GameBot.Service.Models.VersionResolveResultModel {
    Version = new GameBot.Service.Models.SemanticVersionModel {
      Major = resolved.Version.Major,
      Minor = resolved.Version.Minor,
      Patch = resolved.Version.Patch,
      Build = resolved.Version.Build
    },
    Source = buildContext == BuildContext.Ci ? "ci" : "local",
    Persisted = resolved.ShouldPersistBuildCounter,
    Authoritative = resolved.IsAuthoritativeBuild,
    Notes = responseNotes
  });
})
.Accepts<GameBot.Service.Models.VersionResolveRequestModel>("application/json")
.WithTags("Versioning")
.WithName("ResolveVersion");

app.MapPost("/installer/compare", (GameBot.Service.Models.InstallCompareRequestModel request, SemanticVersionComparer comparer) => {
  var installed = new SemanticVersion(
    request.InstalledVersion.Major,
    request.InstalledVersion.Minor,
    request.InstalledVersion.Patch,
    request.InstalledVersion.Build);
  var candidate = new SemanticVersion(
    request.CandidateVersion.Major,
    request.CandidateVersion.Minor,
    request.CandidateVersion.Patch,
    request.CandidateVersion.Build);

  var compare = comparer.Compare(candidate, installed);
  if (compare < 0) {
    return Results.Ok(new GameBot.Service.Models.InstallCompareResultModel {
      Outcome = "downgrade",
      Reason = "Candidate version is lower than installed version.",
      PreserveProperties = false
    });
  }

  if (compare > 0) {
    return Results.Ok(new GameBot.Service.Models.InstallCompareResultModel {
      Outcome = "upgrade",
      Reason = "Candidate version is higher than installed version.",
      PreserveProperties = true
    });
  }

  return Results.Ok(new GameBot.Service.Models.InstallCompareResultModel {
    Outcome = "sameBuild",
    Reason = "Candidate version equals installed version.",
    PreserveProperties = false
  });
})
.Accepts<GameBot.Service.Models.InstallCompareRequestModel>("application/json")
.WithTags("Installer")
.WithName("CompareInstallerVersion");

app.MapPost("/installer/same-build/decision", (GameBot.Service.Models.SameBuildDecisionRequestModel request) => {
  if (string.Equals(request.Mode, "unattended", StringComparison.OrdinalIgnoreCase)) {
    return Results.Ok(new GameBot.Service.Models.SameBuildDecisionResultModel {
      Action = "skip",
      MutatesState = false,
      StatusCode = 4090
    });
  }

  if (string.Equals(request.InteractiveChoice, "reinstall", StringComparison.OrdinalIgnoreCase)) {
    return Results.Ok(new GameBot.Service.Models.SameBuildDecisionResultModel {
      Action = "reinstall",
      MutatesState = true,
      StatusCode = 0
    });
  }

  return Results.Ok(new GameBot.Service.Models.SameBuildDecisionResultModel {
    Action = "cancel",
    MutatesState = false,
    StatusCode = 0
  });
})
.Accepts<GameBot.Service.Models.SameBuildDecisionRequestModel>("application/json")
.WithTags("Installer")
.WithName("ResolveSameBuildDecision");

// Sequences endpoints
var sequences = app.MapGroup(ApiRoutes.Sequences).WithTags("Sequences");

sequences.MapPost("", async (HttpRequest http, ISequenceRepository repo) => {
  using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body).ConfigureAwait(false);
  var root = doc.RootElement;
  if (TryReadFlowRequest(root, out var flowRequest) && flowRequest is not null) {
    var flowGraph = MapToFlowGraph(string.Empty, flowRequest);

    var flowSequence = new GameBot.Domain.Commands.CommandSequence {
      Id = string.Empty,
      Name = flowRequest.Name.Trim(),
      Version = flowRequest.Version > 0 ? flowRequest.Version : 1,
      CreatedAt = DateTimeOffset.UtcNow,
      UpdatedAt = DateTimeOffset.UtcNow
    };
    flowSequence.SetFlowGraph(flowGraph);
    var legacySteps = flowRequest.Steps
      .Where(step => string.Equals(step.StepType, "command", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(step.PayloadRef))
      .Select((step, index) => new GameBot.Domain.Commands.SequenceStep {
        Order = index,
        CommandId = step.PayloadRef!
      });
    flowSequence.SetSteps(legacySteps);

    var createdFlow = await repo.CreateAsync(flowSequence).ConfigureAwait(false);
    return Results.Created(new Uri($"{ApiRoutes.Sequences}/{createdFlow.Id}", UriKind.Relative), ToSequenceResponse(createdFlow));
  }

  // Authoring shape: { name: string, steps?: string[] }
  if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String && !root.TryGetProperty("blocks", out _)) {
    var name = nameProp.GetString()!.Trim();
    var seq = new GameBot.Domain.Commands.CommandSequence { Id = string.Empty, Name = name, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
    if (root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array) {
      var order = 0;
      var steps = new List<GameBot.Domain.Commands.SequenceStep>();
      foreach (var el in stepsProp.EnumerateArray()) {
        if (el.ValueKind == System.Text.Json.JsonValueKind.String) {
          steps.Add(new GameBot.Domain.Commands.SequenceStep { Order = order++, CommandId = el.GetString()! });
        }
      }
      seq.SetSteps(steps);
    }
    var created = await repo.CreateAsync(seq).ConfigureAwait(false);
    return Results.Created(new Uri($"{ApiRoutes.Sequences}/{created.Id}", UriKind.Relative), ToSequenceResponse(created));
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

sequences.MapGet("{sequenceId}", async (ISequenceRepository repo, string sequenceId) => {
  var found = await repo.GetAsync(sequenceId).ConfigureAwait(false);
  if (found is null) return Results.NotFound();
  return Results.Ok(ToSequenceResponse(found));
}).WithName("GetSequence");

sequences.MapPut("{sequenceId}", async (HttpRequest http, ISequenceRepository repo, string sequenceId) => {
  var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
  if (existing is null) return Results.NotFound();
  using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body).ConfigureAwait(false);
  var root = doc.RootElement;
  if (root.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == System.Text.Json.JsonValueKind.Number) {
    var requestedVersion = versionProp.GetInt32();
    if (requestedVersion != existing.Version) {
      return Results.Conflict(new SequenceSaveConflictDto {
        SequenceId = existing.Id,
        CurrentVersion = existing.Version,
        Message = "Sequence has changed. Reload and retry your save."
      });
    }
  }
  if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String) {
    var name = nameProp.GetString()!.Trim();
    if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
  }
  if (TryReadFlowRequest(root, out var flowRequest) && flowRequest is not null) {
    flowRequest.Name = existing.Name;
    var graph = MapToFlowGraph(existing.Id, flowRequest);

    existing.SetFlowGraph(graph);
    var legacySteps = flowRequest.Steps
      .Where(step => string.Equals(step.StepType, "command", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(step.PayloadRef))
      .Select((step, index) => new GameBot.Domain.Commands.SequenceStep {
        Order = index,
        CommandId = step.PayloadRef!
      });
    existing.SetSteps(legacySteps);
  }
  if (root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array) {
    var order = 0;
    var steps = new List<GameBot.Domain.Commands.SequenceStep>();
    foreach (var el in stepsProp.EnumerateArray()) {
      if (el.ValueKind == System.Text.Json.JsonValueKind.String) {
        steps.Add(new GameBot.Domain.Commands.SequenceStep { Order = order++, CommandId = el.GetString()! });
      }
    }
    existing.SetSteps(steps);
  }
  existing.Version += 1;
  existing.UpdatedAt = DateTimeOffset.UtcNow;
  var saved = await repo.UpdateAsync(existing).ConfigureAwait(false);
  return Results.Ok(ToSequenceResponse(saved));
}).WithName("UpdateSequence");

sequences.MapPatch("{sequenceId}", async (HttpRequest http, ISequenceRepository repo, string sequenceId) => {
  var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
  if (existing is null) return Results.NotFound();
  using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body).ConfigureAwait(false);
  var root = doc.RootElement;
  if (root.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == System.Text.Json.JsonValueKind.Number) {
    var requestedVersion = versionProp.GetInt32();
    if (requestedVersion != existing.Version) {
      return Results.Conflict(new SequenceSaveConflictDto {
        SequenceId = existing.Id,
        CurrentVersion = existing.Version,
        Message = "Sequence has changed. Reload and retry your save."
      });
    }
  }
  if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String) {
    var name = nameProp.GetString()!.Trim();
    if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
  }
  if (TryReadFlowRequest(root, out var flowRequest) && flowRequest is not null) {
    flowRequest.Name = existing.Name;
    var graph = MapToFlowGraph(existing.Id, flowRequest);

    existing.SetFlowGraph(graph);
    var legacySteps = flowRequest.Steps
      .Where(step => string.Equals(step.StepType, "command", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(step.PayloadRef))
      .Select((step, index) => new GameBot.Domain.Commands.SequenceStep {
        Order = index,
        CommandId = step.PayloadRef!
      });
    existing.SetSteps(legacySteps);
  }
  if (root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array) {
    var order = 0;
    var steps = new List<GameBot.Domain.Commands.SequenceStep>();
    foreach (var el in stepsProp.EnumerateArray()) {
      if (el.ValueKind == System.Text.Json.JsonValueKind.String) {
        steps.Add(new GameBot.Domain.Commands.SequenceStep { Order = order++, CommandId = el.GetString()! });
      }
    }
    existing.SetSteps(steps);
  }
  existing.Version += 1;
  existing.UpdatedAt = DateTimeOffset.UtcNow;
  var saved = await repo.UpdateAsync(existing).ConfigureAwait(false);
  return Results.Ok(ToSequenceResponse(saved));
}).WithName("PatchSequence");

sequences.MapPost("{sequenceId}/validate", (string sequenceId, SequenceFlowUpsertRequestDto request, ISequenceFlowValidator validator) => {
  var graph = MapToFlowGraph(sequenceId, request);
  var result = validator.Validate(graph);
  if (!result.IsValid) {
    return Results.BadRequest(new { valid = false, errors = result.Errors });
  }

  return Results.Ok(new { valid = true, errors = Array.Empty<string>() });
}).WithName("ValidateSequence");

sequences.MapGet("", async (ISequenceRepository repo) => {
  var list = await repo.ListAsync().ConfigureAwait(false);
  var resp = list.Select(s => new { id = s.Id, name = s.Name, steps = s.Steps.Select(x => x.CommandId).ToArray() });
  return Results.Ok(resp);
}).WithName("ListSequences");

sequences.MapDelete("{sequenceId}", async (ISequenceRepository repo, string sequenceId) => {
  var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
  if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Sequence not found", hint = (string?)null } });
  var ok = await repo.DeleteAsync(sequenceId).ConfigureAwait(false);
  return ok ? Results.NoContent() : Results.NotFound(new { error = new { code = "not_found", message = "Sequence not found", hint = (string?)null } });
}).WithName("DeleteSequence");

sequences.MapPost("{sequenceId}/execute", async (
  GameBot.Domain.Services.SequenceRunner runner,
  TriggerEvaluationService evalSvc,
  GameBot.Service.Services.ExecutionLog.IExecutionLogService executionLogService,
  ISequenceRepository sequenceRepository,
  string sequenceId,
  CancellationToken ct) => {
    // Minimal stub: delegate is a no-op; command execution integration will be added in later phases
    var res = await runner.ExecuteAsync(
      sequenceId,
      _ => Task.CompletedTask,
      gateEvaluator: (step, token) => {
        // Temporary evaluator for integration tests:
        // TargetId "always" => gate passes; "never" => gate fails
        if (step.Gate == null) return Task.FromResult(true);
        var tid = step.Gate.TargetId ?? string.Empty;
        if (string.Equals(tid, "always", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(true);
        if (string.Equals(tid, "never", StringComparison.OrdinalIgnoreCase)) return Task.FromResult(false);
        // Default: pass
        return Task.FromResult(true);
      },
      conditionEvaluator: (cond, token) => {
        // Map Blocks.Condition to a transient Trigger and evaluate via TriggerEvaluationService
        if (string.Equals(cond.Source, "image", StringComparison.OrdinalIgnoreCase)) {
          var region = cond.Region is null ? new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
                                           : new GameBot.Domain.Triggers.Region { X = cond.Region.X, Y = cond.Region.Y, Width = cond.Region.Width, Height = cond.Region.Height };
          var trig = new GameBot.Domain.Triggers.Trigger {
            Id = "inline-image",
            Type = GameBot.Domain.Triggers.TriggerType.ImageMatch,
            Enabled = true,
            Params = new GameBot.Domain.Triggers.ImageMatchParams {
              ReferenceImageId = cond.TargetId,
              Region = region,
              SimilarityThreshold = cond.ConfidenceThreshold ?? 0.85
            }
          };
          var r = evalSvc.Evaluate(trig, DateTimeOffset.UtcNow);
          return Task.FromResult(r.Status == GameBot.Domain.Triggers.TriggerStatus.Satisfied);
        }
        if (string.Equals(cond.Source, "text", StringComparison.OrdinalIgnoreCase)) {
          var region = cond.Region is null ? new GameBot.Domain.Triggers.Region { X = 0, Y = 0, Width = 1, Height = 1 }
                                           : new GameBot.Domain.Triggers.Region { X = cond.Region.X, Y = cond.Region.Y, Width = cond.Region.Width, Height = cond.Region.Height };
          var mode = string.Equals(cond.Mode, "Absent", StringComparison.OrdinalIgnoreCase) ? "not-found" : "found";
          var trig = new GameBot.Domain.Triggers.Trigger {
            Id = "inline-text",
            Type = GameBot.Domain.Triggers.TriggerType.TextMatch,
            Enabled = true,
            Params = new GameBot.Domain.Triggers.TextMatchParams {
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
    var sequence = await sequenceRepository.GetAsync(sequenceId).ConfigureAwait(false);
    var sequenceName = sequence?.Name ?? sequenceId;
    var status = string.Equals(res.Status, "Completed", StringComparison.OrdinalIgnoreCase) ? "success" : "failure";
    await executionLogService.LogSequenceExecutionAsync(
      sequenceId,
      sequenceName,
      status,
      $"Sequence '{sequenceName}' {status} with {res.Steps.Count} executed commands.",
      new GameBot.Service.Services.ExecutionLog.ExecutionLogContext {
        Depth = 0
      },
      details: new[] {
      new GameBot.Domain.Logging.ExecutionDetailItem(
        "sequence",
        $"Executed commands: {string.Join(",", res.Steps.Select(s => s.CommandId).Take(10))}",
        new Dictionary<string, object?> { ["executedCount"] = res.Steps.Count },
        "normal")
      },
      ct).ConfigureAwait(false);
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

void MapLegacyGuard(string legacyRoot, string canonicalRoot) {
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

static string? ReadInstallerNetworkValue(string name) {
  if (!OperatingSystem.IsWindows()) {
    return null;
  }

  const string subKey = @"Software\GameBot\Network";

  var currentUser = Registry.GetValue($@"HKEY_CURRENT_USER\{subKey}", name, null)?.ToString();
  if (!string.IsNullOrWhiteSpace(currentUser)) {
    return currentUser;
  }

  return null;
}

static string? TryFindVersioningDirectory() {
  var directory = new DirectoryInfo(AppContext.BaseDirectory);
  while (directory is not null) {
    var candidate = Path.Combine(directory.FullName, "installer", "versioning");
    if (Directory.Exists(candidate)) {
      return candidate;
    }

    directory = directory.Parent;
  }

  return null;
}

static object ToSequenceResponse(GameBot.Domain.Commands.CommandSequence sequence) {
  if (sequence.FlowSteps.Count > 0 || sequence.FlowLinks.Count > 0) {
    var flowSteps = sequence.FlowSteps.Select(step => new {
      stepId = step.StepId,
      label = step.Label,
      stepType = step.StepType.ToString().ToLowerInvariant(),
      payloadRef = step.PayloadRef,
      iterationLimit = step.IterationLimit,
      condition = MapConditionToDto(step.Condition)
    }).ToArray();

    var flowLinks = sequence.FlowLinks.Select(link => new {
      linkId = link.LinkId,
      sourceStepId = link.SourceStepId,
      targetStepId = link.TargetStepId,
      branchType = link.BranchType.ToString().ToLowerInvariant()
    }).ToArray();

    return new {
      id = sequence.Id,
      name = sequence.Name,
      version = sequence.Version,
      entryStepId = sequence.EntryStepId,
      steps = flowSteps,
      links = flowLinks
    };
  }

  return new {
    id = sequence.Id,
    name = sequence.Name,
    version = sequence.Version,
    steps = sequence.Steps.Select(step => step.CommandId).ToArray()
  };
}

static object? MapConditionToDto(ConditionExpression? expression) {
  if (expression is null) {
    return null;
  }

  return new {
    nodeType = expression.NodeType.ToString().ToLowerInvariant(),
    children = expression.Children.Select(MapConditionToDto).Where(child => child is not null).ToArray(),
    operand = expression.Operand is null
      ? null
      : new {
        operandType = expression.Operand.OperandType.ToString()
          .ToLowerInvariant()
          .Replace("commandoutcome", "command-outcome", StringComparison.Ordinal)
          .Replace("imagedetection", "image-detection", StringComparison.Ordinal),
        targetRef = expression.Operand.TargetRef,
        expectedState = expression.Operand.ExpectedState,
        threshold = expression.Operand.Threshold
      }
  };
}

bool TryReadFlowRequest(System.Text.Json.JsonElement root, out SequenceFlowUpsertRequestDto? request) {
  request = null;
  if (!root.TryGetProperty("entryStepId", out var entryStepIdProp) || entryStepIdProp.ValueKind != JsonValueKind.String) {
    return false;
  }

  if (!root.TryGetProperty("links", out var linksProp) || linksProp.ValueKind != JsonValueKind.Array) {
    return false;
  }

  if (!root.TryGetProperty("steps", out var stepsProp) || stepsProp.ValueKind != JsonValueKind.Array) {
    return false;
  }

  var firstStep = stepsProp.EnumerateArray().FirstOrDefault();
  if (firstStep.ValueKind != JsonValueKind.Object || !firstStep.TryGetProperty("stepId", out _)) {
    return false;
  }

  request = JsonSerializer.Deserialize<SequenceFlowUpsertRequestDto>(
    root.GetRawText(),
    flowRequestJsonOptions);
  return request is not null;
}

static List<string> ValidateSequence(GameBot.Domain.Commands.CommandSequence seq) {
  var errs = new List<string>();
  // Validate blocks if present
  if (seq.Blocks is { Count: > 0 }) {
    foreach (var b in seq.Blocks) {
      ValidateBlock(b, errs, isTopLevel: true);
    }
  }
  return errs;
}

static void ValidateBlock(object blockObj, List<string> errs, bool isTopLevel) {
  if (blockObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object) {
    string? type = null;
    if (je.TryGetProperty("type", out var tProp) && tProp.ValueKind == System.Text.Json.JsonValueKind.String) {
      type = tProp.GetString();
    }
    if (string.IsNullOrWhiteSpace(type)) {
      errs.Add("Block missing required 'type'.");
      return;
    }
    var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "repeatCount", "repeatUntil", "while", "ifElse" };
    if (!allowed.Contains(type)) {
      errs.Add($"Unsupported block type '{type}'.");
      return;
    }

    // Common: steps array for all but else-only
    if (je.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array) {
      foreach (var item in stepsProp.EnumerateArray()) {
        // item can be a Step (object without 'type') or a nested Block (object with 'type')
        if (item.ValueKind == System.Text.Json.JsonValueKind.Object && item.TryGetProperty("type", out var nestedType)) {
          ValidateBlock(item, errs, isTopLevel: false);
        }
      }
    }

    if (type.Equals("ifElse", StringComparison.OrdinalIgnoreCase)) {
      if (!je.TryGetProperty("condition", out var cond) || cond.ValueKind != System.Text.Json.JsonValueKind.Object) {
        errs.Add("ifElse block requires 'condition'.");
      }
      // Validate elseSteps only for ifElse
      if (je.TryGetProperty("elseSteps", out var elseProp) && elseProp.ValueKind == System.Text.Json.JsonValueKind.Array) {
        foreach (var item in elseProp.EnumerateArray()) {
          if (item.ValueKind == System.Text.Json.JsonValueKind.Object && item.TryGetProperty("type", out var nestedType)) {
            ValidateBlock(item, errs, isTopLevel: false);
          }
        }
      }
    }
    else if (type.Equals("repeatUntil", StringComparison.OrdinalIgnoreCase) || type.Equals("while", StringComparison.OrdinalIgnoreCase)) {
      if (!je.TryGetProperty("condition", out var cond) || cond.ValueKind != System.Text.Json.JsonValueKind.Object) {
        errs.Add($"{type} block requires 'condition'.");
      }
      var hasTimeout = je.TryGetProperty("timeoutMs", out var to) && to.ValueKind == System.Text.Json.JsonValueKind.Number && to.GetInt32() >= 0;
      var hasMaxIter = je.TryGetProperty("maxIterations", out var mi) && mi.ValueKind == System.Text.Json.JsonValueKind.Number && mi.GetInt32() >= 1;
      if (!hasTimeout && !hasMaxIter) {
        errs.Add($"{type} block must set 'timeoutMs' or 'maxIterations'.");
      }
      if (je.TryGetProperty("cadenceMs", out var cadence) && cadence.ValueKind == System.Text.Json.JsonValueKind.Number) {
        var c = cadence.GetInt32();
        if (c < 50 || c > 5000) {
          errs.Add($"{type} cadenceMs out of bounds (50-5000): {c}.");
        }
      }
    }
    else if (type.Equals("repeatCount", StringComparison.OrdinalIgnoreCase)) {
      if (!je.TryGetProperty("maxIterations", out var mi) || mi.ValueKind != System.Text.Json.JsonValueKind.Number || mi.GetInt32() < 0) {
        errs.Add("repeatCount requires non-negative 'maxIterations'.");
      }
      if (je.TryGetProperty("cadenceMs", out var cadence) && cadence.ValueKind == System.Text.Json.JsonValueKind.Number) {
        var c = cadence.GetInt32();
        if (c != 0 && (c < 50 || c > 5000)) {
          errs.Add($"repeatCount cadenceMs must be 0 or within 50-5000: {c}.");
        }
      }
    }

    // If a non-ifElse block provides elseSteps, flag as error (T015)
    if (!type.Equals("ifElse", StringComparison.OrdinalIgnoreCase) && je.TryGetProperty("elseSteps", out var elseAny) && elseAny.ValueKind == System.Text.Json.JsonValueKind.Array) {
      errs.Add($"'elseSteps' is only valid for ifElse blocks, not '{type}'.");
    }
  }
}

static SequenceFlowGraph MapToFlowGraph(string sequenceId, SequenceFlowUpsertRequestDto request) {
  var graph = new SequenceFlowGraph {
    SequenceId = sequenceId,
    Name = request.Name,
    Version = request.Version,
    EntryStepId = request.EntryStepId
  };

  graph.SetSteps(request.Steps.Select(step => new FlowStep {
    StepId = step.StepId,
    Label = step.Label,
    StepType = step.StepType.Trim().ToLowerInvariant() switch {
      "action" => FlowStepType.Action,
      "command" => FlowStepType.Command,
      "condition" => FlowStepType.Condition,
      "terminal" => FlowStepType.Terminal,
      _ => FlowStepType.Command
    },
    PayloadRef = step.PayloadRef,
    IterationLimit = step.IterationLimit,
    Condition = MapCondition(step.Condition)
  }));

  graph.SetLinks(request.Links.Select(link => new BranchLink {
    LinkId = link.LinkId,
    SourceStepId = link.SourceStepId,
    TargetStepId = link.TargetStepId,
    BranchType = link.BranchType.Trim().ToLowerInvariant() switch {
      "next" => BranchType.Next,
      "true" => BranchType.True,
      "false" => BranchType.False,
      _ => BranchType.Next
    }
  }));

  return graph;
}

static ConditionExpression? MapCondition(ConditionExpressionDto? dto) {
  if (dto is null) {
    return null;
  }

  var expression = new ConditionExpression {
    NodeType = dto.NodeType.Trim().ToLowerInvariant() switch {
      "and" => ConditionNodeType.And,
      "or" => ConditionNodeType.Or,
      "not" => ConditionNodeType.Not,
      "operand" => ConditionNodeType.Operand,
      _ => ConditionNodeType.Operand
    },
    Operand = dto.Operand is null
      ? null
      : new ConditionOperand {
        OperandType = dto.Operand.OperandType.Trim().ToLowerInvariant() switch {
          "command-outcome" => ConditionOperandType.CommandOutcome,
          "image-detection" => ConditionOperandType.ImageDetection,
          _ => ConditionOperandType.CommandOutcome
        },
        TargetRef = dto.Operand.TargetRef,
        ExpectedState = dto.Operand.ExpectedState,
        Threshold = dto.Operand.Threshold
      }
  };

  if (dto.Children is not null) {
    expression.SetChildren(dto.Children.Select(MapCondition).Where(c => c is not null).Select(c => c!));
  }

  return expression;
}

internal sealed class ConditionalFlowSchemaDocumentFilter : IDocumentFilter {
  public void Apply(Microsoft.OpenApi.Models.OpenApiDocument swaggerDoc, DocumentFilterContext context) {
    _ = swaggerDoc;
    context.SchemaGenerator.GenerateSchema(typeof(SequenceFlowUpsertRequestDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceFlowDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(FlowStepDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(BranchLinkDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(ConditionExpressionDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(ConditionOperandDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceSaveConflictDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(ConditionEvaluationTraceDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(AuthoringDeepLinkDto), context.SchemaRepository);

    AliasSchema(context, nameof(SequenceFlowUpsertRequestDto), "SequenceFlowUpsertRequest");
    AliasSchema(context, nameof(SequenceFlowDto), "SequenceFlow");
    AliasSchema(context, nameof(ConditionExpressionDto), "ConditionExpression");
    AliasSchema(context, nameof(ConditionOperandDto), "ConditionOperand");
    AliasSchema(context, nameof(SequenceSaveConflictDto), "SequenceSaveConflict");
    AliasSchema(context, nameof(ConditionEvaluationTraceDto), "ConditionEvaluationTrace");
    AliasSchema(context, nameof(AuthoringDeepLinkDto), "AuthoringDeepLink");
  }

  private static void AliasSchema(DocumentFilterContext context, string sourceName, string aliasName) {
    if (context.SchemaRepository.Schemas.TryGetValue(sourceName, out var schema)) {
      context.SchemaRepository.Schemas[aliasName] = schema;
    }
  }
}

// For WebApplicationFactory discovery in tests
internal partial class Program { }
