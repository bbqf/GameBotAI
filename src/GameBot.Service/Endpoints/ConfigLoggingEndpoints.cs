using System;
using System.Collections.Generic;
using GameBot.Domain.Services.Logging;
using GameBot.Service.Models.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameBot.Service.Endpoints;

internal static class ConfigLoggingEndpoints
{
    public static IEndpointRouteBuilder MapConfigLoggingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/config/logging");

        group.MapGet("", async (IRuntimeLoggingPolicyService svc, HttpContext ctx) =>
        {
            var snapshot = await svc.GetSnapshotAsync(ctx.RequestAborted).ConfigureAwait(false);
            return Results.Ok(snapshot);
        })
        .WithName("GetLoggingPolicy");

        group.MapPut("components/{componentName}", async (string componentName, LoggingComponentPatchDto patch, IRuntimeLoggingPolicyService svc, HttpContext ctx) =>
        {
            if (patch is null)
            {
                return ValidationError("invalid_payload", "Request body is required.");
            }

            if (patch.Level is null && patch.Enabled is null)
            {
                return ValidationError("invalid_payload", "Specify at least one of 'level' or 'enabled'.");
            }

            var normalizedComponent = componentName?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedComponent))
            {
                return ValidationError("invalid_component", "Component name is required.");
            }

            var actor = ResolveActor(ctx);
            try
            {
                var updated = await svc.SetComponentAsync(normalizedComponent, patch.Level, patch.Enabled, actor, patch.Notes, ctx.RequestAborted).ConfigureAwait(false);
                return Results.Ok(updated);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new
                {
                    error = new { code = "component_not_found", message = $"Component '{normalizedComponent}' is not recognized.", hint = (string?)null }
                });
            }
            catch (ArgumentException ex)
            {
                return ValidationError("invalid_request", ex.Message);
            }
        })
        .WithName("UpdateLoggingComponent");

        group.MapPost("reset", async (LoggingPolicyResetRequestDto? reset, IRuntimeLoggingPolicyService svc, HttpContext ctx) =>
        {
            var actor = ResolveActor(ctx);
            var snapshot = await svc.ResetAsync(actor, reset?.Reason, ctx.RequestAborted).ConfigureAwait(false);
            return Results.Ok(snapshot);
        })
        .WithName("ResetLoggingPolicy");

        return app;
    }

    private static IResult ValidationError(string code, string message) => Results.BadRequest(new
    {
        error = new { code, message, hint = (string?)null }
    });

    private static string ResolveActor(HttpContext ctx)
    {
        var name = ctx.User?.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name!;
        }

        if (ctx.Request.Headers.TryGetValue("X-Actor", out var header))
        {
            var value = header.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        var remote = ctx.Connection.RemoteIpAddress?.ToString();
        return string.IsNullOrWhiteSpace(remote) ? "config-api" : remote!;
    }
}