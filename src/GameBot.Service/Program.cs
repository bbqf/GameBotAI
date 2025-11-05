using GameBot.Service.Security;
using GameBot.Service.Middleware;
using GameBot.Emulator.Session;
using GameBot.Service.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<ISessionManager, SessionManager>();

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
            await next();
            return;
        }

        await TokenAuthMiddleware.Invoke(context, next, authToken!);
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

// For WebApplicationFactory discovery in tests
public partial class Program { }

app.Run();
