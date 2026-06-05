using GameBot.Domain.Commands;
using GameBot.Service.Models;
using GameBot.Service.Services;

namespace GameBot.Service.Endpoints;

internal static class StepsEndpoints {
  public static IEndpointRouteBuilder MapStepEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost(ApiRoutes.Steps + "/execute", async (ExecuteStepRequest req, ICommandExecutor exec, CancellationToken requestCt) => {
      if (req.Step.Type == CommandStepTypeDto.Command) {
        return Results.BadRequest(new {
          error = new {
            code = "invalid_request",
            message = "Command-type steps cannot be executed via this endpoint. Use /force-execute instead.",
            hint = (string?)null
          }
        });
      }

      var validationError = ValidateStep(req.Step);
      if (validationError is not null) {
        return Results.BadRequest(new {
          error = new { code = "invalid_request", message = validationError, hint = (string?)null }
        });
      }

      var domainStep = ToDomainStep(req.Step);

      using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt, timeoutCts.Token);

      try {
        var result = await exec.ForceExecuteStepAsync(req.SessionId, domainStep, linkedCts.Token).ConfigureAwait(false);
        return Results.Accepted((string?)null, new {
          accepted = result.Accepted,
          stepOutcomes = result.StepOutcomes.Select(ToResponseOutcome).ToArray()
        });
      }
      catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested) {
        return Results.Ok(new {
          accepted = 0,
          stepOutcomes = new[] {
            new {
              stepOrder = 0,
              status = "timeout",
              stepType = (string?)null,
              reason = "Step execution timed out after 10 seconds"
            }
          }
        });
      }
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "missing_session_context", StringComparison.OrdinalIgnoreCase)) {
        return Results.BadRequest(new { error = new { code = "missing_session", message = "No session available. Ensure an emulator session is running.", hint = (string?)null } });
      }
      catch (KeyNotFoundException ex) when (ex.Message.Contains("Session", StringComparison.OrdinalIgnoreCase)) {
        return Results.StatusCode(503);
      }
      catch (InvalidOperationException ex) when (string.Equals(ex.Message, "not_running", StringComparison.OrdinalIgnoreCase)) {
        return Results.StatusCode(503);
      }
    })
    .WithName("ExecuteStep")
    .WithTags("Steps");

    return app;
  }

  private static string? ValidateStep(CommandStepDto step) {
    if (step.Type == CommandStepTypeDto.PrimitiveTap) {
      if (step.PrimitiveTap is null || step.PrimitiveTap.DetectionTarget is null || string.IsNullOrWhiteSpace(step.PrimitiveTap.DetectionTarget.ReferenceImageId)) {
        return "primitiveTap.detectionTarget.referenceImageId is required for PrimitiveTap steps";
      }
      return null;
    }

    if (step.Type == CommandStepTypeDto.WaitForImage) {
      if (step.WaitForImage is null) {
        return "waitForImage is required for WaitForImage steps";
      }
      if (step.WaitForImage.TimeoutMs is < 0) {
        return "waitForImage.timeoutMs must be greater than or equal to zero";
      }
      if (step.WaitForImage.DetectionTarget is not null && string.IsNullOrWhiteSpace(step.WaitForImage.DetectionTarget.ReferenceImageId)) {
        return "waitForImage.detectionTarget.referenceImageId must not be empty when detectionTarget is provided";
      }
      return null;
    }

    if (step.Type == CommandStepTypeDto.EnsureGameRunning) {
      return null;
    }

    if (step.Type == CommandStepTypeDto.KeyInput) {
      if (step.KeyInput is null || string.IsNullOrWhiteSpace(step.KeyInput.Key)) {
        return "keyInput.key is required for KeyInput steps";
      }
      return null;
    }

    if (step.Type == CommandStepTypeDto.Swipe) {
      if (step.Swipe is null) {
        return "swipe is required for Swipe steps";
      }
      if (step.Swipe.DurationMs is < 0) {
        return "swipe.durationMs must be greater than or equal to zero";
      }
      return null;
    }

    return null;
  }

  private static CommandStep ToDomainStep(CommandStepDto s) {
    var type = s.Type switch {
      CommandStepTypeDto.WaitForImage => CommandStepType.WaitForImage,
      CommandStepTypeDto.EnsureGameRunning => CommandStepType.EnsureGameRunning,
      CommandStepTypeDto.KeyInput => CommandStepType.KeyInput,
      CommandStepTypeDto.Swipe => CommandStepType.Swipe,
      _ => CommandStepType.PrimitiveTap
    };

    return new CommandStep {
      Type = type,
      TargetId = string.Empty,
      Order = s.Order,
      PrimitiveTap = type == CommandStepType.PrimitiveTap && s.PrimitiveTap?.DetectionTarget is not null
        ? new PrimitiveTapConfig {
          DetectionTarget = new DetectionTarget(
            s.PrimitiveTap.DetectionTarget.ReferenceImageId,
            s.PrimitiveTap.DetectionTarget.Confidence ?? 0.8,
            s.PrimitiveTap.DetectionTarget.OffsetX ?? 0,
            s.PrimitiveTap.DetectionTarget.OffsetY ?? 0,
            DetectionSelectionStrategy.HighestConfidence)
        }
        : null,
      WaitForImage = type == CommandStepType.WaitForImage && s.WaitForImage is not null
        ? new WaitForImageConfig {
          TimeoutMs = s.WaitForImage.TimeoutMs ?? 1000,
          DetectionTarget = s.WaitForImage.DetectionTarget is not null && !string.IsNullOrWhiteSpace(s.WaitForImage.DetectionTarget.ReferenceImageId)
            ? new DetectionTarget(
              s.WaitForImage.DetectionTarget.ReferenceImageId,
              s.WaitForImage.DetectionTarget.Confidence ?? 0.8,
              s.WaitForImage.DetectionTarget.OffsetX ?? 0,
              s.WaitForImage.DetectionTarget.OffsetY ?? 0,
              DetectionSelectionStrategy.HighestConfidence)
            : null
        }
        : null,
      KeyInput = type == CommandStepType.KeyInput && s.KeyInput is not null
        ? new KeyInputConfig { Key = s.KeyInput.Key }
        : null,
      Swipe = type == CommandStepType.Swipe && s.Swipe is not null
        ? new SwipeConfig {
          StartX = s.Swipe.StartX,
          StartY = s.Swipe.StartY,
          EndX = s.Swipe.EndX,
          EndY = s.Swipe.EndY,
          DurationMs = s.Swipe.DurationMs
        }
        : null,
    };
  }

  private static object ToResponseOutcome(GameBot.Service.Services.PrimitiveTapStepOutcome outcome) => new {
    stepOrder = outcome.StepOrder,
    status = outcome.Status,
    stepType = outcome.StepType,
    reason = outcome.Reason,
    detectionConfidence = outcome.DetectionConfidence,
    resolvedPoint = outcome.ResolvedPoint is null ? null : new { x = outcome.ResolvedPoint.X, y = outcome.ResolvedPoint.Y }
  };
}

internal sealed class ExecuteStepRequest {
  public required CommandStepDto Step { get; init; }
  public string? SessionId { get; init; }
}
