using GameBot.Service.Security;
using GameBot.Service.Middleware;
using GameBot.Emulator.Session;
using GameBot.Service.Endpoints;
using GameBot.Domain.Games;
using GameBot.Domain.Profiles;
using Microsoft.OpenApi.Models;

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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "GameBot Service", Version = "v1" });
    // Bearer token scheme so Swagger UI can authorize protected endpoints
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Bearer token. Enter: Bearer <token> (the word Bearer and a space are optional here)",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
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

app.Run();

// For WebApplicationFactory discovery in tests
internal partial class Program { }
