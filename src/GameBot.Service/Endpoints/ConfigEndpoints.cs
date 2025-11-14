using GameBot.Service.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameBot.Service.Endpoints;

internal static class ConfigEndpoints
{
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/config");

        group.MapGet("", async (IConfigSnapshotService svc, HttpContext ctx) =>
        {
            var snap = svc.Current;
            if (snap is null)
            {
                snap = await svc.RefreshAsync(ctx.RequestAborted).ConfigureAwait(false);
            }
            return Results.Ok(snap);
        })
        .WithName("GetConfiguration");

        group.MapPost("refresh", async (IConfigSnapshotService svc, HttpContext ctx) =>
        {
            var snap = await svc.RefreshAsync(ctx.RequestAborted).ConfigureAwait(false);
            return Results.Ok(snap);
        })
        .WithName("RefreshConfiguration");

        return app;
    }
}
