using GameBot.Service.Security;
using GameBot.Service.Middleware;
using GameBot.Emulator.Session;
using GameBot.Service.Endpoints;
using GameBot.Domain.Games;
using GameBot.Domain.Profiles;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<GameBot.Emulator.Session.SessionOptions>(builder.Configuration.GetSection("Service:Sessions"));
builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddTransient<ErrorHandlingMiddleware>();
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Global error handling
app.UseMiddleware<ErrorHandlingMiddleware>();

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

app.Run();

// For WebApplicationFactory discovery in tests
internal partial class Program { }
