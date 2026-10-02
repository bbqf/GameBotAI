using GameBot.Service.Services.StepThrough;
using Microsoft.AspNetCore.Mvc;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The routes of the step-through (feature 127, contracts/step-through-api.md). The routes only map
/// the calls to <see cref="IStepThroughService"/> and map its errors to HTTP statuses.
/// </summary>
internal static class StepThroughEndpoints {
  public static IEndpointRouteBuilder MapStepThroughEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.StepThrough).WithTags("StepThrough");
    group.WithMetadata(new ProducesResponseTypeAttribute(StatusCodes.Status401Unauthorized));

    group.MapPost("", async (StartStepThroughRequest request, IStepThroughService service, CancellationToken ct) => {
      var result = await service.StartAsync(request, ct).ConfigureAwait(false);
      return result.IsSuccess
        ? Results.Created($"{ApiRoutes.StepThrough}/{result.Value!.Id}", result.Value)
        : Error(result.Error!);
    }).WithName("StartStepThrough")
      .Produces<StepThroughStateDto>(StatusCodes.Status201Created);

    group.MapGet("/{id}", (string id, int? afterSeq, IStepThroughService service)
      => Respond(service.Get(id, afterSeq), StatusCodes.Status200OK))
      .WithName("GetStepThrough")
      .Produces<StepThroughStateDto>(StatusCodes.Status200OK);

    group.MapPost("/{id}/run-next", async (string id, IStepThroughService service, CancellationToken ct)
      => Respond(await service.RunNextAsync(id, ct).ConfigureAwait(false), StatusCodes.Status202Accepted))
      .WithName("RunNextStepThroughStep")
      .Produces<StepThroughStateDto>(StatusCodes.Status202Accepted);

    group.MapPost("/{id}/select", async (string id, SelectStepRequest request, IStepThroughService service, CancellationToken ct)
      => Respond(await service.SelectAsync(id, request.Path, ct).ConfigureAwait(false), StatusCodes.Status200OK))
      .WithName("SelectStepThroughStep")
      .Produces<StepThroughStateDto>(StatusCodes.Status200OK);

    group.MapPost("/{id}/cancel", (string id, IStepThroughService service) => {
      var result = service.Cancel(id);
      if (!result.IsSuccess) return Error(result.Error!);
      // 202 when a step ran and now stops. 200 when no step ran.
      return Results.Json(
        result.Value!.State,
        statusCode: result.Value.WasRunning ? StatusCodes.Status202Accepted : StatusCodes.Status200OK);
    }).WithName("CancelStepThroughStep")
      .Produces<StepThroughStateDto>(StatusCodes.Status200OK)
      .Produces<StepThroughStateDto>(StatusCodes.Status202Accepted);

    group.MapPost("/{id}/restart", async (string id, IStepThroughService service, CancellationToken ct)
      => Respond(await service.RestartAsync(id, ct).ConfigureAwait(false), StatusCodes.Status200OK))
      .WithName("RestartStepThrough")
      .Produces<StepThroughStateDto>(StatusCodes.Status200OK);

    group.MapPut("/{id}/values", (string id, SetValuesRequest request, IStepThroughService service)
      => Respond(service.SetValues(id, request), StatusCodes.Status200OK))
      .WithName("SetStepThroughValues")
      .Produces<StepThroughStateDto>(StatusCodes.Status200OK);

    group.MapPost("/{id}/pause-queue", (string id, IStepThroughService service)
      => Respond(service.PauseQueue(id), StatusCodes.Status200OK))
      .WithName("PauseStepThroughQueue")
      .Produces<StepThroughStateDto>(StatusCodes.Status200OK);

    group.MapDelete("/{id}", async (string id, IStepThroughService service) => {
      await service.EndAsync(id).ConfigureAwait(false);
      return Results.NoContent();
    }).WithName("EndStepThrough")
      .Produces(StatusCodes.Status204NoContent);

    return app;
  }

  private static IResult Respond(StepThroughResult<StepThroughStateDto> result, int successStatus)
    => result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : Error(result.Error!);

  private static IResult Error(StepThroughError error) {
    var status = error.Code switch {
      StepThroughErrorCodes.SequenceNotFound or StepThroughErrorCodes.StepThroughNotFound => StatusCodes.Status404NotFound,
      StepThroughErrorCodes.UnsupportedSequenceKind
        or StepThroughErrorCodes.SequenceEmpty
        or StepThroughErrorCodes.UnknownStep
        or StepThroughErrorCodes.NotSelectable
        or StepThroughErrorCodes.UnknownParameter => StatusCodes.Status400BadRequest,
      _ => StatusCodes.Status409Conflict
    };

    return Results.Json(
      new {
        error = new {
          code = error.Code,
          message = error.Message,
          hint = (string?)null,
          details = error.Details
        }
      },
      statusCode: status);
  }
}
