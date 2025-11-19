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

    app.MapPost("/triggers", async (TriggerCreateDto req, ITriggerRepository repo, CancellationToken ct) => {
      if (string.IsNullOrWhiteSpace(req.Type))
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "type is required", hint = (string?)null } });
      if (req.Params is null)
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "params is required", hint = (string?)null } });

      TriggerParams parameters;
      var typeLower = req.Type.Trim().ToLowerInvariant();
      try {
        var elem = (JsonElement)req.Params;
        parameters = typeLower switch {
          "delay" => new DelayParams { Seconds = elem.TryGetProperty("seconds", out var s) ? s.GetInt32() : 0 },
          "schedule" => new ScheduleParams { Timestamp = elem.TryGetProperty("timestamp", out var ts) ? ts.GetDateTimeOffset() : DateTimeOffset.UtcNow },
          "image-match" => new ImageMatchParams {
            ReferenceImageId = elem.TryGetProperty("referenceImageId", out var rid) ? rid.GetString() ?? string.Empty : string.Empty,
            Region = elem.TryGetProperty("region", out var reg) && reg.ValueKind == JsonValueKind.Object
                  ? new Region {
                    X = reg.TryGetProperty("x", out var rx) ? rx.GetDouble() : 0,
                    Y = reg.TryGetProperty("y", out var ry) ? ry.GetDouble() : 0,
                    Width = reg.TryGetProperty("width", out var rw) ? rw.GetDouble() : 0,
                    Height = reg.TryGetProperty("height", out var rh) ? rh.GetDouble() : 0
                  }
                  : new Region { X = 0, Y = 0, Width = 0, Height = 0 },
            SimilarityThreshold = elem.TryGetProperty("similarityThreshold", out var st) ? st.GetDouble() : 0.85
          },
          "text-match" => new TextMatchParams {
            Target = elem.TryGetProperty("target", out var tgt) ? tgt.GetString() ?? string.Empty : string.Empty,
            Region = elem.TryGetProperty("region", out var treg) && treg.ValueKind == JsonValueKind.Object
                  ? new Region {
                    X = treg.TryGetProperty("x", out var rx) ? rx.GetDouble() : 0,
                    Y = treg.TryGetProperty("y", out var ry) ? ry.GetDouble() : 0,
                    Width = treg.TryGetProperty("width", out var rw) ? rw.GetDouble() : 0,
                    Height = treg.TryGetProperty("height", out var rh) ? rh.GetDouble() : 0
                  }
                  : new Region { X = 0, Y = 0, Width = 0, Height = 0 },
            ConfidenceThreshold = elem.TryGetProperty("confidenceThreshold", out var ctElem) ? ctElem.GetDouble() : 0.80,
            Mode = elem.TryGetProperty("mode", out var modeElem) ? modeElem.GetString() ?? "found" : "found",
            Language = elem.TryGetProperty("language", out var langElem) ? langElem.GetString() : null
          },
          _ => throw new InvalidOperationException("unsupported_trigger_type")
        };
      }
      catch (Exception ex) {
        return Results.BadRequest(new { error = new { code = "invalid_params", message = "Failed to parse trigger params", hint = ex.Message } });
      }

      var triggerType = typeLower switch {
        "delay" => TriggerType.Delay,
        "schedule" => TriggerType.Schedule,
        "image-match" => TriggerType.ImageMatch,
        "text-match" => TriggerType.TextMatch,
        _ => TriggerType.Delay
      };

      var trig = new Trigger {
        Id = Guid.NewGuid().ToString("N"),
        Type = triggerType,
        Enabled = req.Enabled,
        CooldownSeconds = req.CooldownSeconds,
        Params = parameters
      };
      await repo.UpsertAsync(trig, ct).ConfigureAwait(false);
      var dto = TriggerMappings.ToDto(trig);
      return Results.Created($"/triggers/{dto.Id}", dto);
    })
    .WithName("CreateTrigger")
    .WithTags("Triggers");

    app.MapGet("/triggers/{id}", async (string id, ITriggerRepository repo, CancellationToken ct) => {
      var trig = await repo.GetAsync(id, ct).ConfigureAwait(false);
      return trig is null
          ? Results.NotFound(new { error = new { code = "not_found", message = "Trigger not found", hint = (string?)null } })
          : Results.Ok(TriggerMappings.ToDto(trig));
    })
    .WithName("GetTrigger")
    .WithTags("Triggers");

    app.MapGet("/triggers", async (ITriggerRepository repo, CancellationToken ct) => {
      var list = await repo.ListAsync(ct).ConfigureAwait(false);
      return Results.Ok(list.Select(TriggerMappings.ToDto));
    })
    .WithName("ListTriggers")
    .WithTags("Triggers");

    app.MapDelete("/triggers/{id}", async (string id, ITriggerRepository repo, CancellationToken ct) => {
      var ok = await repo.DeleteAsync(id, ct).ConfigureAwait(false);
      return ok ? Results.NoContent() : Results.NotFound();
    })
    .WithName("DeleteTrigger")
    .WithTags("Triggers");

    app.MapPost("/triggers/{id}/test", async (string id, ITriggerRepository repo, TriggerEvaluationService evalSvc, CancellationToken ct) => {
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
}
