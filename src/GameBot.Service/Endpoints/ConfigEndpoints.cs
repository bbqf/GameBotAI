using GameBot.Service;
using GameBot.Service.Models;
using GameBot.Service.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameBot.Service.Endpoints;

internal static class ConfigEndpoints {
  public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.Config).WithTags("Configuration");

    group.MapGet("", async (IConfigSnapshotService svc, HttpContext ctx) => {
      var snap = svc.Current;
      if (snap is null) {
        snap = await svc.RefreshAsync(ctx.RequestAborted).ConfigureAwait(false);
      }
      return Results.Ok(snap);
    })
    .WithName("GetConfiguration");

    group.MapPost("refresh", async (IConfigSnapshotService svc, HttpContext ctx) => {
      var snap = await svc.RefreshAsync(ctx.RequestAborted).ConfigureAwait(false);
      return Results.Ok(snap);
    })
    .WithName("RefreshConfiguration");

    group.MapPut("parameters", async (ConfigUpdateRequest req, IConfigSnapshotService svc, HttpContext ctx) => {
      if (req.Updates is null || req.Updates.Count == 0)
        return Results.BadRequest(new { error = new { code = "invalid_payload", message = "At least one parameter update is required.", hint = (string?)null } });
      try {
        var snap = await svc.UpdateParametersAsync(req.Updates, ctx.RequestAborted).ConfigureAwait(false);
        return Results.Ok(snap);
      }
      catch (InvalidOperationException ex) {
        return Results.BadRequest(new { error = new { code = "invalid_request", message = ex.Message, hint = "Environment parameters can only be changed by modifying host environment variables." } });
      }
    })
    .WithName("UpdateConfigParameters");

    group.MapPut("parameters/reorder", async (ConfigReorderRequest req, IConfigSnapshotService svc, HttpContext ctx) => {
      if (req.OrderedKeys is null || req.OrderedKeys.Length == 0)
        return Results.BadRequest(new { error = new { code = "invalid_payload", message = "At least one parameter key is required.", hint = (string?)null } });
      var snap = await svc.ReorderParametersAsync(req.OrderedKeys, ctx.RequestAborted).ConfigureAwait(false);
      return Results.Ok(snap);
    })
    .WithName("ReorderConfigParameters");

    return app;
  }
}
