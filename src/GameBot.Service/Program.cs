using GameBot.Service.Security;
using GameBot.Service.Middleware;
using GameBot.Service.Endpoints;
using GameBot.Service.Logging;
using System.Runtime.InteropServices;
using GameBot.Domain.Vision;
using GameBot.Service.Swagger;
using GameBot.Service;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

// All service registrations, logging setup, and URL binding live in
// GameBotServiceSetup so the top-level Main stays small (huge top-level
// method bodies make Roslyn dataflow analyzers pathologically slow).
var storageRoot = GameBotServiceSetup.ConfigureServices(builder);

// Configuration binding for auth token (env: GAMEBOT_AUTH_TOKEN)
var authToken = builder.Configuration["Service:Auth:Token"]
                 ?? Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");

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
// Commands endpoints (protected if token set)
app.MapCommandEndpoints();
app.MapStepEndpoints();
app.MapQueueEndpoints();
app.MapQueueTemplateEndpoints();
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
app.MapConfigFilesEndpoints(storageRoot);
app.MapCoverageEndpoints();
app.MapExecutionLogEndpoints();
app.MapBackupRestoreEndpoints();

// Versioning + installer endpoints
app.MapVersioningEndpoints();

// Sequences endpoints
app.MapSequenceEndpoints();

// Legacy guard rails: respond with guidance instead of serving old roots
app.MapLegacyGuardEndpoints();

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

// For WebApplicationFactory discovery in tests
internal partial class Program { }
