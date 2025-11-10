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

        group.MapGet("/", (string profileId, ILoggerFactory lf, CancellationToken ct) =>
            Results.Ok(Array.Empty<ProfileTriggerDto>()))
            .WithName("ListProfileTriggers");

        group.MapPost("/", (string profileId, ProfileTriggerCreateDto dto, ILoggerFactory lf, CancellationToken ct) =>
        {
            var created = new ProfileTrigger
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = TriggerType.Delay,
                Enabled = dto.Enabled,
                CooldownSeconds = dto.CooldownSeconds,
                Params = new DelayParams { Seconds = 1 }
            };
            return Results.Created($"/profiles/{profileId}/triggers/{created.Id}", TriggerMappings.ToDto(created));
        }).WithName("CreateProfileTrigger");

        group.MapGet("/{triggerId}", (string profileId, string triggerId, CancellationToken ct) => Results.NotFound())
             .WithName("GetProfileTrigger");

        group.MapPatch("/{triggerId}", (string profileId, string triggerId, HttpRequest req, CancellationToken ct) => Results.NotFound())
             .WithName("PatchProfileTrigger");

        group.MapDelete("/{triggerId}", (string profileId, string triggerId, CancellationToken ct) => Results.NoContent())
             .WithName("DeleteProfileTrigger");

        group.MapPost("/{triggerId}/test", (string profileId, string triggerId, TriggerEvaluationService svc, CancellationToken ct) =>
        {
            var res = new TriggerEvaluationResult { Status = TriggerStatus.Pending, EvaluatedAt = DateTimeOffset.UtcNow, Reason = "stub" };
            return Results.Ok(res);
        }).WithName("TestProfileTrigger");

        group.MapPost("/evaluate", (string profileId, CancellationToken ct) =>
            Results.Ok(Array.Empty<TriggerEvaluationResult>()))
            .WithName("EvaluateProfileTriggers");

        return app;
    }
}
