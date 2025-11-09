using GameBot.Domain.Profiles;
using GameBot.Domain.Services;
using GameBot.Service.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Endpoints;

internal static class TriggersEndpoints
{
    public static IEndpointRouteBuilder MapTriggersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/profiles/{profileId}/triggers");

        group.MapGet("/", async (string profileId, ILoggerFactory lf, CancellationToken ct) =>
        {
            // Placeholder: list triggers from repository once wired
            return Results.Ok(Array.Empty<ProfileTriggerDto>());
        });

        group.MapPost("/", async (string profileId, ProfileTriggerCreateDto dto, ILoggerFactory lf, CancellationToken ct) =>
        {
            // Placeholder: validate and create trigger
            var created = new ProfileTrigger
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = TriggerType.Delay,
                Enabled = dto.Enabled,
                CooldownSeconds = dto.CooldownSeconds,
                Params = new DelayParams { Seconds = 1 }
            };
            return Results.Created($"/profiles/{profileId}/triggers/{created.Id}", TriggerMappings.ToDto(created));
        });

        group.MapGet("/{triggerId}", async (string profileId, string triggerId, CancellationToken ct) =>
        {
            return Results.NotFound();
        });

        group.MapPatch("/{triggerId}", async (string profileId, string triggerId, HttpRequest req, CancellationToken ct) =>
        {
            return Results.NotFound();
        });

        group.MapDelete("/{triggerId}", async (string profileId, string triggerId, CancellationToken ct) =>
        {
            return Results.NoContent();
        });

        group.MapPost("/{triggerId}/test", async (string profileId, string triggerId, TriggerEvaluationService svc, CancellationToken ct) =>
        {
            // Placeholder: return pending result
            var res = new TriggerEvaluationResult { Status = TriggerStatus.Pending, EvaluatedAt = DateTimeOffset.UtcNow, Reason = "stub" };
            return Results.Ok(res);
        });

        group.MapPost("/evaluate", async (string profileId, CancellationToken ct) =>
        {
            // Placeholder: batch evaluate
            return Results.Ok(Array.Empty<TriggerEvaluationResult>());
        });

        return app;
    }
}
