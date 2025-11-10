using GameBot.Domain.Profiles;
using GameBot.Domain.Services;
using System.Text.Json;
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

        group.MapGet("/", async (string profileId, IProfileRepository repo, CancellationToken ct) =>
        {
            var prof = await repo.GetAsync(profileId, ct).ConfigureAwait(false);
            if (prof is null) return Results.NotFound();
            var dtos = prof.Triggers.Select(TriggerMappings.ToDto).ToArray();
            return Results.Ok(dtos);
        })
            .WithName("ListProfileTriggers");

        group.MapPost("/", async (string profileId, ProfileTriggerCreateDto dto, IProfileRepository repo, CancellationToken ct) =>
        {
            var prof = await repo.GetAsync(profileId, ct).ConfigureAwait(false);
            if (prof is null) return Results.NotFound();
            var trigger = MapCreateDto(dto);
            trigger.EnabledAt = trigger.Enabled ? DateTimeOffset.UtcNow : null;
            prof.Triggers.Add(trigger);
            await repo.UpdateAsync(prof, ct).ConfigureAwait(false);
            return Results.Created($"/profiles/{profileId}/triggers/{trigger.Id}", TriggerMappings.ToDto(trigger));
        }).WithName("CreateProfileTrigger");

        group.MapGet("/{triggerId}", async (string profileId, string triggerId, IProfileRepository repo, CancellationToken ct) =>
        {
            var prof = await repo.GetAsync(profileId, ct).ConfigureAwait(false);
            if (prof is null) return Results.NotFound();
            var trig = prof.Triggers.FirstOrDefault(t => t.Id == triggerId);
            return trig is null ? Results.NotFound() : Results.Ok(TriggerMappings.ToDto(trig));
        })
             .WithName("GetProfileTrigger");

        group.MapPatch("/{triggerId}", async (string profileId, string triggerId, HttpRequest req, IProfileRepository repo, CancellationToken ct) =>
        {
            var prof = await repo.GetAsync(profileId, ct).ConfigureAwait(false);
            if (prof is null) return Results.NotFound();
            var trig = prof.Triggers.FirstOrDefault(t => t.Id == triggerId);
            if (trig is null) return Results.NotFound();
            using var doc = await JsonDocument.ParseAsync(req.Body, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.TryGetProperty("enabled", out var enabledEl) && enabledEl.ValueKind == JsonValueKind.True || enabledEl.ValueKind == JsonValueKind.False)
            {
                var newEnabled = enabledEl.GetBoolean();
                if (trig.Enabled != newEnabled)
                {
                    trig.Enabled = newEnabled;
                    trig.EnabledAt = newEnabled ? DateTimeOffset.UtcNow : null;
                }
            }
            if (root.TryGetProperty("cooldownSeconds", out var cdEl) && cdEl.ValueKind == JsonValueKind.Number && cdEl.TryGetInt32(out var cd))
            {
                trig.CooldownSeconds = Math.Max(0, cd);
            }
            // Simple params patch for Delay (seconds) and Schedule (timestamp)
            if (root.TryGetProperty("params", out var paramsEl))
            {
                if (trig.Params is DelayParams d && paramsEl.TryGetProperty("seconds", out var secEl) && secEl.ValueKind == JsonValueKind.Number && secEl.TryGetInt32(out var sec))
                {
                    d.Seconds = Math.Max(0, sec);
                }
                if (trig.Params is ScheduleParams s && paramsEl.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.String)
                {
                    if (DateTimeOffset.TryParse(tsEl.GetString(), out var ts)) s.Timestamp = ts;
                }
            }
            await repo.UpdateAsync(prof, ct).ConfigureAwait(false);
            return Results.Ok(TriggerMappings.ToDto(trig));
        })
             .WithName("PatchProfileTrigger");

        group.MapDelete("/{triggerId}", async (string profileId, string triggerId, IProfileRepository repo, CancellationToken ct) =>
        {
            var prof = await repo.GetAsync(profileId, ct).ConfigureAwait(false);
            if (prof is null) return Results.NotFound();
            var removed = prof.Triggers.FirstOrDefault(t => t.Id == triggerId);
            if (removed is null) return Results.NotFound();
            prof.Triggers.Remove(removed);
            await repo.UpdateAsync(prof, ct).ConfigureAwait(false);
            return Results.NoContent();
        })
             .WithName("DeleteProfileTrigger");

        group.MapPost("/{triggerId}/test", async (string profileId, string triggerId, IProfileRepository repo, TriggerEvaluationService svc, CancellationToken ct) =>
        {
            var prof = await repo.GetAsync(profileId, ct).ConfigureAwait(false);
            if (prof is null) return Results.NotFound();
            var trig = prof.Triggers.FirstOrDefault(t => t.Id == triggerId);
            if (trig is null) return Results.NotFound();
            var res = svc.Evaluate(trig, DateTimeOffset.UtcNow);
            trig.LastResult = res;
            trig.LastEvaluatedAt = res.EvaluatedAt;
            if (res.Status == TriggerStatus.Satisfied)
            {
                trig.LastFiredAt = res.EvaluatedAt;
            }
            await repo.UpdateAsync(prof, ct).ConfigureAwait(false);
            return Results.Ok(res);
        }).WithName("TestProfileTrigger");

        group.MapPost("/evaluate", async (string profileId, IProfileRepository repo, TriggerEvaluationService svc, CancellationToken ct) =>
        {
            var prof = await repo.GetAsync(profileId, ct).ConfigureAwait(false);
            if (prof is null) return Results.NotFound();
            var results = new List<TriggerEvaluationResult>();
            foreach (var trig in prof.Triggers.Where(t => t.Enabled))
            {
                var r = svc.Evaluate(trig, DateTimeOffset.UtcNow);
                trig.LastResult = r;
                trig.LastEvaluatedAt = r.EvaluatedAt;
                if (r.Status == TriggerStatus.Satisfied)
                {
                    trig.LastFiredAt = r.EvaluatedAt;
                }
                results.Add(r);
            }
            await repo.UpdateAsync(prof, ct).ConfigureAwait(false);
            return Results.Ok(results);
        })
            .WithName("EvaluateProfileTriggers");

        return app;
    }

    private static ProfileTrigger MapCreateDto(ProfileTriggerCreateDto dto)
    {
        var id = Guid.NewGuid().ToString("N");
        var typeParsed = Enum.TryParse<TriggerType>(dto.Type, true, out var tt) ? tt : TriggerType.Delay;
        TriggerParams p = typeParsed switch
        {
            TriggerType.Delay => new DelayParams { Seconds =  ((JsonElement)dto.Params).GetProperty("seconds").GetInt32() },
            TriggerType.Schedule => new ScheduleParams { Timestamp = DateTimeOffset.Parse(((JsonElement)dto.Params).GetProperty("timestamp").GetString()!, System.Globalization.CultureInfo.InvariantCulture) },
            _ => new DelayParams { Seconds = 1 }
        };
        return new ProfileTrigger
        {
            Id = id,
            Type = typeParsed,
            Enabled = dto.Enabled,
            CooldownSeconds = dto.CooldownSeconds,
            Params = p
        };
    }
}
