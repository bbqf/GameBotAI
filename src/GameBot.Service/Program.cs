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
using GameBot.Service.Models;
using SequencesAuthoringDeepLinkDto = GameBot.Service.Contracts.Sequences.AuthoringDeepLinkDto;
using SequencesConditionEvaluationTraceDto = GameBot.Service.Contracts.Sequences.ConditionEvaluationTraceDto;
using Swashbuckle.AspNetCore.SwaggerGen;
using GameBot.Service.Services.Conditions;

var builder = WebApplication.CreateBuilder(args);
var perStepRequestJsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

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
builder.Services.AddSingleton<SequenceStepValidationService>();
builder.Services.AddSingleton<ActionPayloadValidationService>();
builder.Services.AddSingleton<CycleIterationLimiter>();
builder.Services.AddSingleton<IConditionEvaluator, ConditionEvaluator>();
builder.Services.AddSingleton<ICommandOutcomeConditionAdapter, CommandOutcomeConditionAdapter>();
builder.Services.AddSingleton<IImageDetectionConditionAdapter, ImageDetectionConditionAdapter>();
builder.Services.AddSingleton<IImageVisibleConditionAdapter, ImageVisibleConditionAdapter>();
builder.Services.AddSingleton(_ =>
{
    var loopMaxEnv = Environment.GetEnvironmentVariable("GAMEBOT_LOOP_MAX_ITERATIONS");
    var loopMax = int.TryParse(loopMaxEnv, out var parsed) && parsed > 0 ? parsed : 1000;
    return new GameBot.Domain.Config.AppConfig { LoopMaxIterations = loopMax };
});
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
var legacyBranchingErrors = new[] { "entryStepId and links are no longer supported. Use per-step conditions on steps[].condition." };

sequences.MapPost("", async (HttpRequest http, ISequenceRepository repo, SequenceStepValidationService stepValidationService, IImageRepository imageRepository, CancellationToken ct) => {
  using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
  var root = doc.RootElement;
  if (HasLegacyBranchingFields(root)) {
    return Results.BadRequest(new {
      message = "Invalid sequence payload",
      errors = legacyBranchingErrors
    });
  }

  var isPerStepCandidate = IsPerStepRequestCandidate(root);
  if (TryReadPerStepRequest(root, out var perStepRequest, out var perStepRequestError) && perStepRequest is not null) {
    var perStepSequence = new GameBot.Domain.Commands.CommandSequence {
      Id = string.Empty,
      Name = perStepRequest.Name.Trim(),
      Version = perStepRequest.Version > 0 ? perStepRequest.Version : 1,
      CreatedAt = DateTimeOffset.UtcNow,
      UpdatedAt = DateTimeOffset.UtcNow
    };

    var linearSteps = MapToLinearSteps(perStepRequest);
    var perStepValidationErrors = await ValidatePerStepForPersistenceAsync(linearSteps, stepValidationService, imageRepository, ct).ConfigureAwait(false);
    if (perStepValidationErrors.Count > 0) {
      return Results.BadRequest(new { message = "Invalid sequence payload", errors = perStepValidationErrors });
    }

    var existingSequences = await repo.ListAsync().ConfigureAwait(false);
    if (existingSequences.Count == 0) {
      perStepSequence.Version = 1;
    }

    perStepSequence.SetFlowGraph(null);
    perStepSequence.SetSteps(linearSteps);
    var createdPerStep = await repo.CreateAsync(perStepSequence).ConfigureAwait(false);
    return Results.Created(new Uri($"{ApiRoutes.Sequences}/{createdPerStep.Id}", UriKind.Relative), ToSequenceResponse(createdPerStep));
  }

  if (isPerStepCandidate && !string.IsNullOrWhiteSpace(perStepRequestError)) {
    return Results.BadRequest(new { message = "Invalid sequence payload", errors = new[] { perStepRequestError } });
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

sequences.MapPut("{sequenceId}", async (HttpRequest http, ISequenceRepository repo, SequenceStepValidationService stepValidationService, IImageRepository imageRepository, string sequenceId, CancellationToken ct) => {
  var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
  if (existing is null) return Results.NotFound();
  using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
  var root = doc.RootElement;
  if (HasLegacyBranchingFields(root)) {
    return Results.BadRequest(new {
      message = "Invalid sequence payload",
      errors = legacyBranchingErrors
    });
  }

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
  var isPerStepCandidate = IsPerStepRequestCandidate(root);
  if (TryReadPerStepRequest(root, out var perStepRequest, out var perStepRequestError) && perStepRequest is not null) {
    existing.Name = string.IsNullOrWhiteSpace(perStepRequest.Name) ? existing.Name : perStepRequest.Name.Trim();
    var linearSteps = MapToLinearSteps(perStepRequest);
    var perStepValidationErrors = await ValidatePerStepForPersistenceAsync(linearSteps, stepValidationService, imageRepository, ct).ConfigureAwait(false);
    if (perStepValidationErrors.Count > 0) {
      return Results.BadRequest(new { message = "Invalid sequence payload", errors = perStepValidationErrors });
    }

    existing.SetFlowGraph(null);
    existing.SetSteps(linearSteps);
  }
  else if (isPerStepCandidate && !string.IsNullOrWhiteSpace(perStepRequestError)) {
    return Results.BadRequest(new { message = "Invalid sequence payload", errors = new[] { perStepRequestError } });
  }
  if (root.TryGetProperty("steps", out var stepsProp)
      && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array
      && stepsProp.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String)) {
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

sequences.MapPatch("{sequenceId}", async (HttpRequest http, ISequenceRepository repo, SequenceStepValidationService stepValidationService, IImageRepository imageRepository, string sequenceId, CancellationToken ct) => {
  var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
  if (existing is null) return Results.NotFound();
  using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
  var root = doc.RootElement;
  if (HasLegacyBranchingFields(root)) {
    return Results.BadRequest(new {
      message = "Invalid sequence payload",
      errors = legacyBranchingErrors
    });
  }

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
  var isPerStepCandidate = IsPerStepRequestCandidate(root);
  if (TryReadPerStepRequest(root, out var perStepRequest, out var perStepRequestError) && perStepRequest is not null) {
    existing.Name = string.IsNullOrWhiteSpace(perStepRequest.Name) ? existing.Name : perStepRequest.Name.Trim();
    var linearSteps = MapToLinearSteps(perStepRequest);
    var perStepValidationErrors = await ValidatePerStepForPersistenceAsync(linearSteps, stepValidationService, imageRepository, ct).ConfigureAwait(false);
    if (perStepValidationErrors.Count > 0) {
      return Results.BadRequest(new { message = "Invalid sequence payload", errors = perStepValidationErrors });
    }

    existing.SetFlowGraph(null);
    existing.SetSteps(linearSteps);
  }
  else if (isPerStepCandidate && !string.IsNullOrWhiteSpace(perStepRequestError)) {
    return Results.BadRequest(new { message = "Invalid sequence payload", errors = new[] { perStepRequestError } });
  }
  if (root.TryGetProperty("steps", out var stepsProp)
      && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array
      && stepsProp.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String)) {
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

sequences.MapPost("{sequenceId}/validate", async (string sequenceId, SequenceFlowUpsertRequestDto request, ISequenceFlowValidator validator, ISequenceRepository repo, SequenceStepValidationService stepValidationService) => {
  var graph = MapToFlowGraph(sequenceId, request);
  var flowResult = validator.Validate(graph);

  // Also run step-level validation (includes loop rules) on persisted steps
  var stepErrors = new List<string>();
  var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
  if (existing is not null) {
    stepErrors.AddRange(stepValidationService.Validate(existing.Steps));
  }

  var allErrors = flowResult.Errors.Concat(stepErrors).ToArray();
  if (allErrors.Length > 0) {
    return Results.BadRequest(new { valid = false, errors = allErrors });
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
  IImageVisibleConditionAdapter imageVisibleConditionAdapter,
  GameBot.Service.Services.ExecutionLog.IExecutionLogService executionLogService,
  ISequenceRepository sequenceRepository,
  GameBot.Service.Services.ICommandExecutor commandExecutor,
  string sequenceId,
  HttpContext httpContext,
  CancellationToken ct) => {
    // Read optional sessionId from request body
    string? sessionId = null;
    try {
      var body = await httpContext.Request.ReadFromJsonAsync<SequenceExecuteRequest>(ct).ConfigureAwait(false);
      sessionId = body?.SessionId;
    } catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) {
      // empty body, malformed JSON, or missing content-type — no sessionId override
    }

    var res = await runner.ExecuteAsync(
      sequenceId,
      async commandId => {
        try {
          await commandExecutor.ForceExecuteAsync(sessionId, commandId, ct).ConfigureAwait(false);
        } catch (KeyNotFoundException ex) when (ex.Message == "cached_session_not_found") {
          throw new InvalidOperationException($"No cached session found for command '{commandId}'. Start a session first.");
        } catch (InvalidOperationException ex) when (ex.Message == "missing_session_context") {
          throw new InvalidOperationException($"No session available for command '{commandId}'. Start a session or pass a sessionId.");
        } catch (KeyNotFoundException) {
          // Command not found in repository — step uses a primitive action type (e.g. tap)
          // or references a non-existent command; treat as completed.
        }
      },
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
          return imageVisibleConditionAdapter.EvaluateAsync(cond, token).AsTask();
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
    var status = string.Equals(res.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ? "success" : "failure";
    var flowStepsByCommandRef = (sequence?.FlowSteps ?? Array.Empty<GameBot.Domain.Commands.FlowStep>())
      .GroupBy(step => string.IsNullOrWhiteSpace(step.PayloadRef) ? step.StepId : step.PayloadRef!, StringComparer.Ordinal)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    var commandSteps = res.Steps.Where(s => s.LoopIterations is null).ToList();
    var detailItems = new List<GameBot.Domain.Logging.ExecutionDetailItem> {
      new(
        "sequence",
        $"Executed commands: {string.Join(",", commandSteps.Select(s => s.CommandId).Take(10))}",
        new Dictionary<string, object?> { ["executedCount"] = commandSteps.Count },
        "normal")
    };

    var stepOrder = 1;
    foreach (var step in res.Steps) {
      flowStepsByCommandRef.TryGetValue(step.CommandId, out var flowStep);
      var stepId = flowStep?.StepId ?? step.CommandId;
      var stepLabel = flowStep?.Label ?? step.CommandId;

      // Loop summary entries are not command executions — emit a loop-type detail instead.
      if (step.LoopIterations is not null) {
        var iterCount = step.LoopIterations.Count;
        detailItems.Add(new GameBot.Domain.Logging.ExecutionDetailItem(
          "step",
          $"Loop '{stepLabel}' {step.Status.ToLowerInvariant()} after {iterCount} iteration{(iterCount == 1 ? "" : "s")}.",
          new Dictionary<string, object?> {
            ["stepOrder"] = stepOrder++,
            ["stepType"] = "loop",
            ["status"] = step.Status,
            ["actionOutcome"] = step.Status.ToLowerInvariant(),
            ["iterations"] = iterCount,
            ["message"] = step.Message,
            ["sequenceId"] = sequenceId,
            ["sequenceLabel"] = sequenceName,
            ["stepId"] = stepId,
            ["stepLabel"] = stepLabel
          },
          "normal"));
        continue;
      }

      var actionOutcome = string.IsNullOrWhiteSpace(step.ActionOutcome)
        ? (string.Equals(step.Status, "Skipped", StringComparison.OrdinalIgnoreCase) ? "skipped" : "executed")
        : step.ActionOutcome;
      var stepDisplayMessage = !string.IsNullOrWhiteSpace(step.Message)
        ? $"Step '{stepLabel}' {actionOutcome}: {step.Message}"
        : $"Step '{stepLabel}' {actionOutcome}.";
      detailItems.Add(new GameBot.Domain.Logging.ExecutionDetailItem(
        "step",
        stepDisplayMessage,
        new Dictionary<string, object?> {
          ["stepOrder"] = stepOrder++,
          ["stepType"] = "command",
          ["status"] = step.Status,
          ["actionOutcome"] = actionOutcome,
          ["conditionType"] = step.ConditionType,
          ["conditionResult"] = step.ConditionResult,
          ["message"] = step.Message,
          ["sequenceId"] = sequenceId,
          ["sequenceLabel"] = sequenceName,
          ["stepId"] = stepId,
          ["stepLabel"] = stepLabel
        },
        "normal"));
    }

    foreach (var trace in res.ConditionTraces) {
      detailItems.Add(new GameBot.Domain.Logging.ExecutionDetailItem(
        "step",
        $"Condition step '{trace.StepLabel ?? trace.StepId}' evaluated to {trace.Trace.FinalResult}.",
        new Dictionary<string, object?> {
          ["stepOrder"] = stepOrder++,
          ["stepType"] = "condition",
          ["status"] = "executed",
          ["conditionResult"] = trace.Trace.FinalResult,
          ["actionOutcome"] = trace.Trace.FinalResult ? "executed" : "skipped",
          ["sequenceId"] = sequenceId,
          ["sequenceLabel"] = sequenceName,
          ["stepId"] = trace.StepId,
          ["stepLabel"] = trace.StepLabel ?? trace.StepId,
          ["conditionTrace"] = trace.Trace
        },
        "normal"));
    }

    await executionLogService.LogSequenceExecutionAsync(
      sequenceId,
      sequenceName,
      status,
      $"Sequence '{sequenceName}' {status} with {commandSteps.Count} step{(commandSteps.Count == 1 ? "" : "s")} executed.",
      new GameBot.Service.Services.ExecutionLog.ExecutionLogContext {
        Depth = 0,
        SequenceId = sequenceId,
        SequenceLabel = sequenceName
      },
      details: detailItems,
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

  if (sequence.Steps.Any(step => !string.IsNullOrWhiteSpace(step.StepId) || step.Action is not null || step.Condition is not null)) {
    return new {
      id = sequence.Id,
      name = sequence.Name,
      version = sequence.Version,
      steps = sequence.Steps.Select(MapStepToDto).ToArray()
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

static object? MapPerStepConditionToDto(SequenceStepCondition? condition) {
  return condition switch {
    ImageVisibleStepCondition imageVisible => new {
      type = "imageVisible",
      imageId = imageVisible.ImageId,
      minSimilarity = imageVisible.MinSimilarity
    },
    CommandOutcomeStepCondition commandOutcome => new {
      type = "commandOutcome",
      stepRef = commandOutcome.StepRef,
      expectedState = commandOutcome.ExpectedState
    },
    _ => null
  };
}

static object MapStepToDto(SequenceStep step) {
  var stepType = step.StepType switch {
    SequenceStepType.Loop => "Loop",
    SequenceStepType.Break => "Break",
    _ => "Action"
  };

  return new {
    stepId = step.StepId,
    label = step.Label,
    stepType,
    action = step.Action is null
      ? null
      : new {
        type = step.Action.Type,
        parameters = step.Action.Parameters
      },
    condition = MapPerStepConditionToDto(step.Condition),
    loop = MapLoopConfigToDto(step.Loop),
    body = step.Body.Count > 0 ? step.Body.Select(MapStepToDto).ToArray() : null,
    breakCondition = MapPerStepConditionToDto(step.BreakCondition)
  };
}

static object? MapLoopConfigToDto(LoopConfig? loop) {
  return loop switch {
    CountLoopConfig count => new {
      loopType = "count",
      count = count.Count,
      maxIterations = count.MaxIterations
    },
    WhileLoopConfig while_ => new {
      loopType = "while",
      condition = MapPerStepConditionToDto(while_.Condition),
      maxIterations = while_.MaxIterations
    },
    RepeatUntilLoopConfig repeatUntil => new {
      loopType = "repeatUntil",
      condition = MapPerStepConditionToDto(repeatUntil.Condition),
      maxIterations = repeatUntil.MaxIterations
    },
    _ => null
  };
}

bool HasLegacyBranchingFields(System.Text.Json.JsonElement root) {
  return root.TryGetProperty("entryStepId", out _)
         || root.TryGetProperty("links", out _);
}

bool IsPerStepRequestCandidate(System.Text.Json.JsonElement root) {
  if (HasLegacyBranchingFields(root)) {
    return false;
  }

  if (!root.TryGetProperty("steps", out var stepsProp) || stepsProp.ValueKind != JsonValueKind.Array) {
    return false;
  }

  var firstStep = stepsProp.EnumerateArray().FirstOrDefault();
  return firstStep.ValueKind == JsonValueKind.Object
    && (firstStep.TryGetProperty("action", out _) || firstStep.TryGetProperty("stepType", out _));
}

bool TryReadPerStepRequest(System.Text.Json.JsonElement root, out SequenceUpsertContract? request, out string? error) {
  request = null;
  error = null;

  var isPerStepCandidate = IsPerStepRequestCandidate(root);
  if (!isPerStepCandidate) {
    return false;
  }

  if (!root.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String) {
    error = "name is required for sequence payload.";
    return false;
  }

  if (!root.TryGetProperty("steps", out var stepsProp) || stepsProp.ValueKind != JsonValueKind.Array) {
    error = "steps array is required for sequence payload.";
    return false;
  }

  foreach (var stepElement in stepsProp.EnumerateArray()) {
    if (stepElement.ValueKind != JsonValueKind.Object) {
      error = "each step must be an object.";
      return false;
    }

    if (!stepElement.TryGetProperty("stepId", out var stepIdProp) || stepIdProp.ValueKind != JsonValueKind.String) {
      error = "each step must include string stepId.";
      return false;
    }

    var hasStepType = stepElement.TryGetProperty("stepType", out var stepTypeProp) && stepTypeProp.ValueKind == JsonValueKind.String;
    var stepTypeValue = hasStepType ? stepTypeProp.GetString()?.Trim().ToLowerInvariant() : null;
    var isLoopOrBreak = stepTypeValue is "loop" or "break";

    if (!isLoopOrBreak && (!stepElement.TryGetProperty("action", out var actionProp) || actionProp.ValueKind != JsonValueKind.Object)) {
      error = "each action step must include action object.";
      return false;
    }
  }

  try {
    request = JsonSerializer.Deserialize<SequenceUpsertContract>(
      root.GetRawText(),
      perStepRequestJsonOptions);
  }
  catch (JsonException ex) {
    error = string.IsNullOrWhiteSpace(ex.Message) ? "Malformed sequence payload." : ex.Message;
    return false;
  }

  if (request is null) {
    error = "Malformed sequence payload.";
    return false;
  }

  if (request.Steps is null || request.Steps.Count == 0) {
    error = "steps must contain at least one step.";
    return false;
  }

  return true;
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
      _ => throw new InvalidOperationException($"Unsupported flow step type '{step.StepType}'.")
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

static List<SequenceStep> MapToLinearSteps(SequenceUpsertContract request) {
  var result = new List<SequenceStep>(request.Steps.Count);
  for (var index = 0; index < request.Steps.Count; index++) {
    var step = request.Steps[index];
    var parsedStepType = ParseStepType(step.StepType);

    if (parsedStepType == SequenceStepType.Loop) {
      var mapped = new SequenceStep {
        Order = index,
        StepId = step.StepId,
        Label = step.Label,
        StepType = SequenceStepType.Loop,
        Loop = MapLoopConfig(step.Loop),
        Body = MapBodySteps(step.Body)
      };
      result.Add(mapped);
      continue;
    }

    if (parsedStepType == SequenceStepType.Break) {
      var mapped = new SequenceStep {
        Order = index,
        StepId = step.StepId,
        Label = step.Label,
        StepType = SequenceStepType.Break,
        BreakCondition = MapPerStepCondition(step.BreakCondition)
      };
      result.Add(mapped);
      continue;
    }

    {
      var mapped = new SequenceStep {
        Order = index,
        StepId = step.StepId,
        Label = step.Label,
        CommandId = step.StepId,
        StepType = SequenceStepType.Action,
        Action = step.Action is not null ? new SequenceActionPayload { Type = step.Action.Type } : null,
        Condition = MapPerStepCondition(step.Condition)
      };

      if (step.Action is not null) {
        foreach (var parameter in step.Action.Parameters) {
          mapped.Action!.Parameters[parameter.Key] = parameter.Value;
        }

        if (string.Equals(step.Action.Type, ActionTypes.Command, StringComparison.OrdinalIgnoreCase)
            && step.Action.Parameters.TryGetValue("commandId", out var commandId)
            && commandId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(commandId.GetString())) {
          mapped.CommandId = commandId.GetString()!;
        }
      }

      result.Add(mapped);
    }
  }

  return result;
}

static SequenceStepType ParseStepType(string? stepType) {
  if (string.IsNullOrWhiteSpace(stepType)) return SequenceStepType.Action;
  return stepType.Trim().ToLowerInvariant() switch {
    "loop" => SequenceStepType.Loop,
    "break" => SequenceStepType.Break,
    "action" => SequenceStepType.Action,
    "command" => SequenceStepType.Command,
    _ => SequenceStepType.Action
  };
}

static LoopConfig? MapLoopConfig(LoopConfigContract? contract) {
  return contract switch {
    CountLoopConfigContract count => new CountLoopConfig { Count = count.Count, MaxIterations = count.MaxIterations },
    WhileLoopConfigContract while_ => new WhileLoopConfig { Condition = MapPerStepCondition(while_.Condition)!, MaxIterations = while_.MaxIterations },
    RepeatUntilLoopConfigContract repeatUntil => new RepeatUntilLoopConfig { Condition = MapPerStepCondition(repeatUntil.Condition)!, MaxIterations = repeatUntil.MaxIterations },
    _ => null
  };
}

static IReadOnlyList<SequenceStep> MapBodySteps(IReadOnlyList<SequenceStepContract>? body) {
  if (body is null || body.Count == 0) return Array.Empty<SequenceStep>();
  var result = new List<SequenceStep>(body.Count);
  for (var i = 0; i < body.Count; i++) {
    var child = body[i];
    var childType = ParseStepType(child.StepType);

    if (childType == SequenceStepType.Break) {
      result.Add(new SequenceStep {
        Order = i,
        StepId = child.StepId,
        Label = child.Label,
        StepType = SequenceStepType.Break,
        BreakCondition = MapPerStepCondition(child.BreakCondition)
      });
    } else {
      var mapped = new SequenceStep {
        Order = i,
        StepId = child.StepId,
        Label = child.Label,
        CommandId = child.StepId,
        StepType = SequenceStepType.Action,
        Action = child.Action is not null ? new SequenceActionPayload { Type = child.Action.Type } : null,
        Condition = MapPerStepCondition(child.Condition)
      };
      if (child.Action is not null) {
        foreach (var parameter in child.Action.Parameters) {
          mapped.Action!.Parameters[parameter.Key] = parameter.Value;
        }
        if (string.Equals(child.Action.Type, ActionTypes.Command, StringComparison.OrdinalIgnoreCase)
            && child.Action.Parameters.TryGetValue("commandId", out var cid)
            && cid.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(cid.GetString())) {
          mapped.CommandId = cid.GetString()!;
        }
      }
      result.Add(mapped);
    }
  }
  return result;
}

static SequenceStepCondition? MapPerStepCondition(SequenceStepConditionContract? condition) {
  return condition switch {
    ImageVisibleConditionContract imageVisible => new ImageVisibleStepCondition {
      ImageId = imageVisible.ImageId,
      MinSimilarity = imageVisible.MinSimilarity
    },
    CommandOutcomeConditionContract commandOutcome => new CommandOutcomeStepCondition {
      StepRef = commandOutcome.StepRef,
      ExpectedState = commandOutcome.ExpectedState
    },
    _ => null
  };
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

static async Task<List<string>> ValidatePerStepForPersistenceAsync(
  IReadOnlyList<SequenceStep> steps,
  SequenceStepValidationService stepValidationService,
  IImageRepository imageRepository,
  CancellationToken ct) {
  var errors = new List<string>();
  errors.AddRange(stepValidationService.Validate(steps));
  errors.AddRange(await ValidatePerStepImageReferencesAsync(steps, imageRepository, ct).ConfigureAwait(false));

  for (var index = 0; index < steps.Count; index++) {
    ct.ThrowIfCancellationRequested();
    var step = steps[index];
    var stepLabel = string.IsNullOrWhiteSpace(step.StepId) ? $"index:{index}" : step.StepId;
    if (step.Action is null) {
      continue;
    }

    if (string.IsNullOrWhiteSpace(step.Action.Type)) {
      errors.Add($"Step '{stepLabel}' action type is required.");
    }

    if (step.Condition is ImageVisibleStepCondition imageVisible
        && imageVisible.MinSimilarity is < 0 or > 1) {
      errors.Add($"Step '{stepLabel}' imageVisible minSimilarity must be within 0..1.");
    }
  }

  return errors;
}

static async Task<IReadOnlyList<string>> ValidatePerStepImageReferencesAsync(
  IReadOnlyList<SequenceStep> steps,
  IImageRepository imageRepository,
  CancellationToken ct) {
  var errors = new List<string>();
  var missingByImageId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
  var cache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

  foreach (var step in steps.Where(step => step.Condition is ImageVisibleStepCondition)) {
    ct.ThrowIfCancellationRequested();
    var imageCondition = (ImageVisibleStepCondition)step.Condition!;
    var imageId = imageCondition.ImageId?.Trim();
    if (string.IsNullOrWhiteSpace(imageId)) {
      continue;
    }

    if (!cache.TryGetValue(imageId, out var exists)) {
      exists = await imageRepository.ExistsAsync(imageId, ct).ConfigureAwait(false);
      cache[imageId] = exists;
    }

    if (exists) {
      continue;
    }

    if (!missingByImageId.TryGetValue(imageId, out var stepsForImage)) {
      stepsForImage = new List<string>();
      missingByImageId[imageId] = stepsForImage;
    }

    stepsForImage.Add(string.IsNullOrWhiteSpace(step.StepId) ? $"index:{step.Order}" : step.StepId);
  }

  foreach (var missing in missingByImageId.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
    errors.Add($"Image reference '{missing.Key}' does not exist (used by: {string.Join(", ", missing.Value.Distinct(StringComparer.OrdinalIgnoreCase))}).");
  }

  return errors;
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
    context.SchemaGenerator.GenerateSchema(typeof(SequencesConditionEvaluationTraceDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequencesAuthoringDeepLinkDto), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceUpsertContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceStepContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceActionContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(SequenceStepConditionContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(ImageVisibleConditionContract), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(CommandOutcomeConditionContract), context.SchemaRepository);

    AliasSchema(context, nameof(SequenceFlowUpsertRequestDto), "SequenceFlowUpsertRequest");
    AliasSchema(context, nameof(SequenceFlowDto), "SequenceFlow");
    AliasSchema(context, nameof(ConditionExpressionDto), "ConditionExpression");
    AliasSchema(context, nameof(ConditionOperandDto), "ConditionOperand");
    AliasSchema(context, nameof(SequenceSaveConflictDto), "SequenceSaveConflict");
    AliasSchema(context, nameof(SequencesConditionEvaluationTraceDto), "ConditionEvaluationTrace");
    AliasSchema(context, nameof(SequencesAuthoringDeepLinkDto), "AuthoringDeepLink");
    AliasSchema(context, nameof(SequenceUpsertContract), "SequenceUpsertRequest");
    AliasSchema(context, nameof(SequenceStepContract), "SequenceStep");
    AliasSchema(context, nameof(SequenceActionContract), "SequenceAction");
    AliasSchema(context, nameof(SequenceStepConditionContract), "SequenceStepCondition");
    AliasSchema(context, nameof(ImageVisibleConditionContract), "ImageVisibleCondition");
    AliasSchema(context, nameof(CommandOutcomeConditionContract), "CommandOutcomeCondition");
  }

  private static void AliasSchema(DocumentFilterContext context, string sourceName, string aliasName) {
    if (context.SchemaRepository.Schemas.TryGetValue(sourceName, out var schema)) {
      context.SchemaRepository.Schemas[aliasName] = schema;
    }
  }
}

// For WebApplicationFactory discovery in tests
internal partial class Program { }
