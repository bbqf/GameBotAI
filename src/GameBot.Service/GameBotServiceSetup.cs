using GameBot.Emulator.Session;
using GameBot.Service.Middleware;
using GameBot.Domain.Games;
using GameBot.Domain.Triggers;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using GameBot.Domain.Logging;
using GameBot.Domain.Services.Logging;
using GameBot.Service.Hosted;
using GameBot.Service.Logging;
using GameBot.Service.Services.Ocr;
using GameBot.Service.Services;
using System.Text.Json.Serialization;
using GameBot.Service.Services.Detections;
using GameBot.Service.Swagger;
using GameBot.Domain.Images;
using Microsoft.Win32;
using GameBot.Domain.Versioning;
using GameBot.Service.Services.Conditions;

namespace GameBot.Service;

// Startup composition: service registrations and web-host URL binding extracted
// from Program.cs so the implicit top-level Main stays small (huge top-level
// method bodies make Roslyn dataflow analyzers pathologically slow).
internal static class GameBotServiceSetup {
  // Registers all GameBot services and configures logging and Kestrel URLs.
  // Returns the resolved data storage root, which endpoint mapping needs later.
  public static string ConfigureServices(WebApplicationBuilder builder) {
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
    builder.Services.AddControllers().AddJsonOptions(o => {
      o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
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
    // SessionService: optionally inject BackgroundScreenCaptureService when ADB capture is enabled
    builder.Services.AddSingleton<ISessionService>(sp => {
      var sessions = sp.GetRequiredService<ISessionManager>();
      var cache = sp.GetRequiredService<ISessionContextCache>();
      var captureService = sp.GetService<GameBot.Emulator.Session.BackgroundScreenCaptureService>();
      return new SessionService(sessions, cache, captureService);
    });
    builder.Services.AddTransient<ErrorHandlingMiddleware>();
    builder.Services.AddTransient<CorrelationIdMiddleware>();
    builder.Services.AddSingleton<GameBot.Service.Services.EnsureGameRunning.IAdbGameOperations, GameBot.Service.Services.EnsureGameRunning.AdbGameOperations>();
    builder.Services.AddSingleton<GameBot.Service.Services.EnsureGameRunning.IEnsureGameRunningActionHandler, GameBot.Service.Services.EnsureGameRunning.EnsureGameRunningActionHandler>();
    builder.Services.AddSingleton<GameBot.Service.Services.ICommandExecutor, GameBot.Service.Services.CommandExecutor>();
    builder.Services.AddSingleton<GameBot.Service.Services.BackupService>();

    // Data storage configuration (env: GAMEBOT_DATA_DIR or config Service:Storage:Root)
    var storageRoot = builder.Configuration["Service:Storage:Root"]
                      ?? Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")
                      ?? Path.Combine(AppContext.BaseDirectory, "data");
    Directory.CreateDirectory(storageRoot);

    builder.Services.AddSingleton<IGameRepository>(_ => new FileGameRepository(storageRoot));
    builder.Services.AddSingleton<ITriggerRepository>(_ => new FileTriggerRepository(storageRoot));
    builder.Services.AddSingleton<ICommandRepository>(_ => new FileCommandRepository(storageRoot));
    builder.Services.AddSingleton<ISequenceRepository>(_ => new FileSequenceRepository(storageRoot));
    builder.Services.AddSingleton<GameBot.Domain.Queues.IQueueRepository>(_ => new GameBot.Domain.Queues.FileQueueRepository(storageRoot));
    builder.Services.AddSingleton<GameBot.Domain.Queues.IQueueRuntimeStore, GameBot.Domain.Queues.QueueRuntimeStore>();
    builder.Services.AddSingleton<GameBot.Domain.QueueTemplates.IQueueTemplateRepository>(_ => new GameBot.Domain.QueueTemplates.FileQueueTemplateRepository(storageRoot));
    builder.Services.AddSingleton<IExecutionLogRepository>(_ => new FileExecutionLogRepository(storageRoot));
    builder.Services.AddSingleton<IExecutionLogRetentionPolicyRepository>(_ => new ExecutionLogRetentionPolicyRepository(storageRoot));
    builder.Services.AddSingleton<GameBot.Service.Services.ExecutionLog.IExecutionLogService, GameBot.Service.Services.ExecutionLog.ExecutionLogService>();
    builder.Services.AddSingleton<GameBot.Service.Services.SequenceExecution.ISequenceExecutionService, GameBot.Service.Services.SequenceExecution.SequenceExecutionService>();
    builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
    // Self-reschedule (feature 065): the run registry breaks the SequenceExecutionService ↔
    // QueueExecutionService DI cycle; the coordinator injects ephemeral run-scoped firings.
    builder.Services.AddSingleton<GameBot.Service.Services.QueueExecution.IQueueRunRegistry, GameBot.Service.Services.QueueExecution.QueueRunRegistry>();
    builder.Services.AddSingleton<GameBot.Service.Services.QueueExecution.ISelfRescheduleCoordinator, GameBot.Service.Services.QueueExecution.SelfRescheduleCoordinator>();
    builder.Services.AddSingleton<GameBot.Service.Services.QueueExecution.IQueueExecutionService, GameBot.Service.Services.QueueExecution.QueueExecutionService>();
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
    builder.Services.AddSingleton(_ => {
      var loopMaxEnv = Environment.GetEnvironmentVariable("GAMEBOT_LOOP_MAX_ITERATIONS");
      var loopMax = int.TryParse(loopMaxEnv, out var parsed) && parsed > 0 ? parsed : 1000;

      var captureIntervalEnv = Environment.GetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS");
      var captureInterval = int.TryParse(captureIntervalEnv, out var ciParsed) && ciParsed > 0 ? Math.Max(ciParsed, 50) : 500;

      var retryCountEnv = Environment.GetEnvironmentVariable("GAMEBOT_TAP_RETRY_COUNT");
      var retryCount = int.TryParse(retryCountEnv, out var rcParsed) && rcParsed >= 0 ? rcParsed : 3;

      var retryProgressionEnv = Environment.GetEnvironmentVariable("GAMEBOT_TAP_RETRY_PROGRESSION");
      var retryProgression = double.TryParse(retryProgressionEnv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rpParsed) && rpParsed > 0 ? rpParsed : 1.0;

      var tapJitterEnv = Environment.GetEnvironmentVariable("GAMEBOT_TAP_JITTER_RADIUS_PX");
      var tapJitterRadius = int.TryParse(tapJitterEnv, out var tjParsed) && tjParsed >= 0 ? tjParsed : 5;

      var adbRetriesEnv = Environment.GetEnvironmentVariable("GAMEBOT_ADB_RETRIES");
      var adbRetries = int.TryParse(adbRetriesEnv, out var arParsed) && arParsed >= 0 ? arParsed : 2;

      var adbRetryDelayEnv = Environment.GetEnvironmentVariable("GAMEBOT_ADB_RETRY_DELAY_MS");
      var adbRetryDelay = int.TryParse(adbRetryDelayEnv, out var ardParsed) && ardParsed >= 0 ? ardParsed : 100;

      return new GameBot.Domain.Config.AppConfig {
        LoopMaxIterations = loopMax,
        CaptureIntervalMs = captureInterval,
        TapRetryCount = retryCount,
        TapRetryProgression = retryProgression,
        TapJitterRadiusPx = tapJitterRadius,
        AdbRetries = adbRetries,
        AdbRetryDelayMs = adbRetryDelay,
      };
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
    builder.Services.AddSingleton<GameBot.Service.Services.IConfigApplier>(sp =>
        new GameBot.Service.Services.ConfigApplier(
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitorCache<GameBot.Service.Hosted.TriggerWorkerOptions>>(),
            sp.GetRequiredService<GameBot.Domain.Config.AppConfig>(),
            sp.GetService<GameBot.Emulator.Session.BackgroundScreenCaptureService>()));
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
        // Background capture service: per-session ADB capture loops with configurable interval
        builder.Services.AddSingleton<GameBot.Emulator.Session.BackgroundScreenCaptureService>(sp => {
          var appConfig = sp.GetRequiredService<GameBot.Domain.Config.AppConfig>();
          var adbLogger = sp.GetRequiredService<ILogger<GameBot.Emulator.Adb.AdbClient>>();
          Func<string, GameBot.Emulator.Session.IAdbScreenCaptureProvider> factory = serial =>
            new GameBot.Emulator.Session.AdbScreenCaptureProvider(serial, adbLogger);
          return new GameBot.Emulator.Session.BackgroundScreenCaptureService(
            factory, appConfig.CaptureIntervalMs, sp.GetRequiredService<ILogger<GameBot.Emulator.Session.BackgroundScreenCaptureService>>());
        });
        // IScreenSource backed by background capture cache (replaces direct ADB + TTL cache chain)
        builder.Services.AddSingleton<GameBot.Domain.Triggers.Evaluators.IScreenSource>(sp => {
          var captureService = sp.GetRequiredService<GameBot.Emulator.Session.BackgroundScreenCaptureService>();
          var sessions = sp.GetRequiredService<ISessionManager>();
          return new GameBot.Emulator.Session.BackgroundCaptureScreenSource(captureService, sessions);
        });
        // Keep AdbScreenSource registered for any legacy/direct consumers
        builder.Services.AddSingleton<GameBot.Emulator.Session.AdbScreenSource>();
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

    ConfigureWebHostUrls(builder);

    return storageRoot;
  }

  // In CI/tests (or when explicitly requested), avoid fixed ports to prevent socket bind conflicts
  private static void ConfigureWebHostUrls(WebApplicationBuilder builder) {
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
  }

  private static string? ReadInstallerNetworkValue(string name) {
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
}
