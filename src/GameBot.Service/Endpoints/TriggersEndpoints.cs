using System.Text.Json;
using System.Text.Json.Serialization;
using GameBot.Domain.Triggers;
using GameBot.Domain.Services;
using GameBot.Service.Models;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.OpenApi;

namespace GameBot.Service.Endpoints;

internal static class TriggersEndpoints {
  public static IEndpointRouteBuilder MapTriggerEndpoints(this IEndpointRouteBuilder app) {
    ArgumentNullException.ThrowIfNull(app);

    app.MapPost("/api/triggers", async (HttpRequest http, ITriggerRepository repo, CancellationToken ct) => {
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      // Authoring shape: { name, criteria: { type, ... }, actions?:[], commands?:[], sequence?:string }
      if (root.TryGetProperty("criteria", out var criteria) && criteria.ValueKind == JsonValueKind.Object) {
        var typeLower = criteria.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String ? tEl.GetString()!.Trim().ToLowerInvariant() : "delay";
        var (triggerType, parameters) = ParseCriteriaToDomain(typeLower, criteria);
        var trig = new Trigger {
          Id = Guid.NewGuid().ToString("N"),
          Type = triggerType,
          Enabled = true,
          CooldownSeconds = 0,
          Params = parameters
        };
        await repo.UpsertAsync(trig, ct).ConfigureAwait(false);
        var name = root.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String ? nEl.GetString()! : typeLower;
        var actions = root.TryGetProperty("actions", out var aEl) && aEl.ValueKind == JsonValueKind.Array ? aEl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray() : Array.Empty<string>();
        var commands = root.TryGetProperty("commands", out var cEl) && cEl.ValueKind == JsonValueKind.Array ? cEl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray() : Array.Empty<string>();
        var sequence = root.TryGetProperty("sequence", out var sEl) && sEl.ValueKind == JsonValueKind.String ? sEl.GetString() : null;
        return Results.Created($"/api/triggers/{trig.Id}", new { id = trig.Id, name, criteria = criteria, actions, commands, sequence });
      }

      // Domain shape fallback
      var req = root.Deserialize<TriggerCreateDto>();
      if (req is null || string.IsNullOrWhiteSpace(req.Type) || req.Params is null)
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "type and params are required", hint = (string?)null } });

      var elem = (JsonElement)req.Params;
      var typeLowerFallback = req.Type.Trim().ToLowerInvariant();
      var (tt, parametersFallback) = ParseCriteriaToDomain(typeLowerFallback, elem);
      var trigFallback = new Trigger { Id = Guid.NewGuid().ToString("N"), Type = tt, Enabled = req.Enabled, CooldownSeconds = req.CooldownSeconds, Params = parametersFallback };
      await repo.UpsertAsync(trigFallback, ct).ConfigureAwait(false);
      var dto = TriggerMappings.ToDto(trigFallback);
      return Results.Created($"/api/triggers/{dto.Id}", dto);
    })
    .WithName("CreateTrigger")
    .WithTags("Triggers");

    app.MapGet("/api/triggers/{id}", async (string id, ITriggerRepository repo, CancellationToken ct) => {
      var trig = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (trig is null) return Results.NotFound(new { error = new { code = "not_found", message = "Trigger not found", hint = (string?)null } });
      var criteria = BuildCriteriaFromDomain(trig);
      var name = GetTypeString(trig.Type);
      return Results.Ok(new { id = trig.Id, name, criteria });
    })
    .WithName("GetTrigger")
    .WithTags("Triggers");

    app.MapGet("/api/triggers", async (ITriggerRepository repo, CancellationToken ct) => {
      var list = await repo.ListAsync(ct).ConfigureAwait(false);
      var resp = list.Select(t => new { id = t.Id, name = GetTypeString(t.Type), criteria = BuildCriteriaFromDomain(t) });
      return Results.Ok(resp);
    })
    .WithName("ListTriggers")
    .WithTags("Triggers");

    app.MapPut("/api/triggers/{id}", async (string id, HttpRequest http, ITriggerRepository repo, CancellationToken ct) => {
      var existing = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (existing is null) return Results.NotFound();
      using var doc = await JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      if (root.TryGetProperty("criteria", out var criteria) && criteria.ValueKind == JsonValueKind.Object) {
        var typeLower = criteria.TryGetProperty("type", out var tEl) && tEl.ValueKind == JsonValueKind.String ? tEl.GetString()!.Trim().ToLowerInvariant() : GetTypeString(existing.Type);
        var (tt, p) = ParseCriteriaToDomain(typeLower, criteria);
        existing.Type = tt;
        existing.Params = p;
      }
      await repo.UpsertAsync(existing, ct).ConfigureAwait(false);
      return Results.Ok(new { id = existing.Id, name = GetTypeString(existing.Type), criteria = BuildCriteriaFromDomain(existing) });
    }).WithName("UpdateTrigger").WithTags("Triggers");

    app.MapDelete("/api/triggers/{id}", async (string id, ITriggerRepository repo, CancellationToken ct) => {
      var ok = await repo.DeleteAsync(id, ct).ConfigureAwait(false);
      return ok ? Results.NoContent() : Results.NotFound();
    })
    .WithName("DeleteTrigger")
    .WithTags("Triggers");

    app.MapPost("/api/triggers/{id}/test", async (string id, ITriggerRepository repo, TriggerEvaluationService evalSvc, CancellationToken ct) => {
      var trig = await repo.GetAsync(id, ct).ConfigureAwait(false);
      if (trig is null)
        return Results.NotFound(new { error = new { code = "not_found", message = "Trigger not found", hint = (string?)null } });
      var res = evalSvc.Evaluate(trig, DateTimeOffset.UtcNow);
      trig.LastResult = res;
      trig.LastEvaluatedAt = res.EvaluatedAt;
      if (res.Status == TriggerStatus.Satisfied)
        trig.LastFiredAt = res.EvaluatedAt;
      await repo.UpsertAsync(trig, ct).ConfigureAwait(false);
      return Results.Ok(res);
    })
    .WithName("TestTrigger")
    .WithTags("Triggers");

    return app;
  }

  private static (TriggerType type, TriggerParams parameters) ParseCriteriaToDomain(string typeLower, JsonElement elem) {
    switch (typeLower) {
      case "delay":
        return (TriggerType.Delay, new DelayParams { Seconds = elem.TryGetProperty("seconds", out var s) ? s.GetInt32() : 0 });
      case "schedule":
        return (TriggerType.Schedule, new ScheduleParams { Timestamp = elem.TryGetProperty("timestamp", out var ts) ? ts.GetDateTimeOffset() : DateTimeOffset.UtcNow });
      case "image-match":
        var reg = elem.TryGetProperty("region", out var r) && r.ValueKind == JsonValueKind.Object ? r : default;
        var region = reg.ValueKind == JsonValueKind.Object
          ? new Region {
              X = reg.TryGetProperty("x", out var imx) ? imx.GetDouble() : 0,
              Y = reg.TryGetProperty("y", out var imy) ? imy.GetDouble() : 0,
              Width = reg.TryGetProperty("width", out var imw) ? imw.GetDouble() : 0,
              Height = reg.TryGetProperty("height", out var imh) ? imh.GetDouble() : 0
            }
          : new Region { X = 0, Y = 0, Width = 0, Height = 0 };
        return (TriggerType.ImageMatch, new ImageMatchParams {
          ReferenceImageId = elem.TryGetProperty("referenceImageId", out var rid) ? rid.GetString() ?? string.Empty : string.Empty,
          Region = region,
          SimilarityThreshold = elem.TryGetProperty("similarityThreshold", out var st) ? st.GetDouble() : 0.85
        });
      case "text-match":
        var treg = elem.TryGetProperty("region", out var rr) && rr.ValueKind == JsonValueKind.Object ? rr : default;
        var region2 = treg.ValueKind == JsonValueKind.Object
          ? new Region {
              X = treg.TryGetProperty("x", out var tx) ? tx.GetDouble() : 0,
              Y = treg.TryGetProperty("y", out var ty) ? ty.GetDouble() : 0,
              Width = treg.TryGetProperty("width", out var tw) ? tw.GetDouble() : 0,
              Height = treg.TryGetProperty("height", out var th) ? th.GetDouble() : 0
            }
          : new Region { X = 0, Y = 0, Width = 0, Height = 0 };
        return (TriggerType.TextMatch, new TextMatchParams {
          Target = elem.TryGetProperty("target", out var tgt) ? tgt.GetString() ?? string.Empty : string.Empty,
          Region = region2,
          ConfidenceThreshold = elem.TryGetProperty("confidenceThreshold", out var ctElem) ? ctElem.GetDouble() : 0.80,
          Mode = elem.TryGetProperty("mode", out var modeElem) ? modeElem.GetString() ?? "found" : "found",
          Language = elem.TryGetProperty("language", out var langElem) ? langElem.GetString() : null
        });
      default:
        return (TriggerType.Delay, new DelayParams { Seconds = elem.TryGetProperty("seconds", out var sec) ? sec.GetInt32() : 0 });
    }
  }

  private static object BuildCriteriaFromDomain(Trigger t) {
    var typeString = GetTypeString(t.Type);
    return t.Params switch {
      DelayParams p => new { type = typeString, seconds = p.Seconds },
      ScheduleParams p => new { type = typeString, timestamp = p.Timestamp },
      ImageMatchParams p => new { type = typeString, referenceImageId = p.ReferenceImageId, region = new { x = p.Region.X, y = p.Region.Y, width = p.Region.Width, height = p.Region.Height }, similarityThreshold = p.SimilarityThreshold },
      TextMatchParams p => new { type = typeString, target = p.Target, region = new { x = p.Region.X, y = p.Region.Y, width = p.Region.Width, height = p.Region.Height }, confidenceThreshold = p.ConfidenceThreshold, mode = p.Mode, language = p.Language },
      _ => new { type = typeString }
    };
  }

  private static string GetTypeString(TriggerType type) => type switch {
    TriggerType.Delay => "delay",
    TriggerType.Schedule => "schedule",
    TriggerType.ImageMatch => "image-match",
    TriggerType.TextMatch => "text-match",
    _ => type.ToString().ToLowerInvariant()
  };
}
