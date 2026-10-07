using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Service.Models;
using GameBot.Service.Services.Ocr;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GameBot.Service.Endpoints;

/// <summary>
/// <c>POST /api/ocr/read</c>: reads text from a region of the live screen or of a stored capture.
/// The handler is a named method, not a lambda, because the taint analyzers cost more on large lambdas.
/// </summary>
internal static class OcrReadEndpoints {
  private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web);

  public static IEndpointRouteBuilder MapOcrReadEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost(ApiRoutes.Ocr + "/read", ReadAsync)
      .WithTags("Emulators")
      .Accepts<OcrReadRequest>("application/json")
      .Produces<OcrReadResponse>(StatusCodes.Status200OK)
      .Produces<OcrErrorResponse>(StatusCodes.Status400BadRequest)
      .Produces<OcrErrorResponse>(StatusCodes.Status404NotFound)
      .Produces<OcrErrorResponse>(StatusCodes.Status502BadGateway)
      .Produces<OcrErrorResponse>(StatusCodes.Status503ServiceUnavailable)
      .WithName("ReadOcrRegion")
      .WithSummary("Read text from a region of the screen")
      .WithDescription(
        "Reads the text in a region of the live screen (serial) or of a stored capture (captureId). "
        + "No input is sent to the emulator. Set exactly one source.");
    return app;
  }

  private static async Task<IResult> ReadAsync(HttpContext ctx, CancellationToken ct) {
    OcrReadRequest? request;
    try {
      request = await JsonSerializer.DeserializeAsync<OcrReadRequest>(ctx.Request.Body, BodyOptions, ct).ConfigureAwait(false);
    }
    catch (JsonException) {
      return Error(400, "invalid_request", "The request body is not valid JSON for this endpoint.");
    }

    if (!OperatingSystem.IsWindows()) {
      return Error(503, "ocr_unavailable", "OCR needs a Windows host.");
    }
    var service = ctx.RequestServices.GetService<OcrReadService>();
    if (service is null) {
      return Error(503, "ocr_unavailable", "The OCR read service is not available on this host.");
    }

    var outcome = service.Read(request);
    return outcome.Response is not null
      ? Results.Ok(outcome.Response)
      : Error(outcome.Status, outcome.Code ?? "invalid_request", outcome.Message ?? string.Empty);
  }

  private static IResult Error(int status, string code, string message) =>
    Results.Json(new OcrErrorResponse { Code = code, Message = message }, statusCode: status);
}
