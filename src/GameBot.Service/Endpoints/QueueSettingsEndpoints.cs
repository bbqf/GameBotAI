using System.Text;
using GameBot.Domain.QueueSettings;
using Microsoft.AspNetCore.Http.Features;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The queue settings API (feature 145): export and import by name. The handlers are named methods, because the taint
/// analyzers cost more on large lambdas.
/// </summary>
internal static class QueueSettingsEndpoints {
  /// <summary>The largest import request in bytes (20 MB).</summary>
  internal const long MaxRequestBytes = 20L * 1024 * 1024;

  /// <summary>The body type of the import route. Used for the OpenAPI document only.</summary>
  internal sealed class QueueSettingsImportRequestBody { }

  public static IEndpointRouteBuilder MapQueueSettingsEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.QueueSettings).WithTags("QueueSettings");

    group.MapGet("export", ExportAsync)
      .Produces<SettingsDocument>(StatusCodes.Status200OK)
      .WithName("ExportQueueSettings")
      .WithSummary("Export the queue settings and the artifacts of the user")
      .WithDescription(
        "Gives one JSON document with the settings of all queues, all queue templates, and all sequences and commands "
        + "that no gamepack owns. All links use names. The document has no id and no time stamp of the instance. "
        + "An object that a gamepack owns is only a name reference in the references list.");

    group.MapPost("import", ImportAsync)
      .Accepts<QueueSettingsImportRequestBody>("application/json")
      .Produces<ImportReport>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
      .ProducesProblem(StatusCodes.Status500InternalServerError)
      .WithName("ImportQueueSettings")
      .WithSummary("Import queue settings by name")
      .WithDescription(
        "Checks the whole document first. Then it creates or updates queues, queue templates, sequences, and commands by name. "
        + "It never changes an object that a gamepack owns, and it never deletes an object. A second import of the same "
        + "document changes nothing. A link whose target is missing does not fail the import: the report lists it. "
        + "If a store write fails, the status is 500 and the report shows what the import applied, what failed, and what it "
        + "did not try. Send the same document again to finish. The request can have at most 20 MB.");

    return app;
  }

  private static async Task<IResult> ExportAsync(QueueSettingsExporter exporter, HttpContext http, CancellationToken ct) {
    var document = await exporter.ExportAsync(ct).ConfigureAwait(false);
    http.Response.Headers.ContentDisposition = "attachment; filename=\"gamebot-settings.json\"";
    return Results.Text(SettingsDocumentJson.Serialize(document), "application/json", Encoding.UTF8);
  }

  private static async Task<IResult> ImportAsync(HttpRequest http, QueueSettingsImporter importer, CancellationToken ct) {
    string json;
    try {
      var body = await ReadBodyAsync(http, ct).ConfigureAwait(false);
      if (body is null) {
        return TooLarge();
      }

      json = body;
    }
    catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge) {
      return TooLarge();
    }

    var outcome = await importer.ImportAsync(json, ct).ConfigureAwait(false);
    return outcome.Status switch {
      ImportStatus.Ok => Results.Json(outcome.Report, SettingsDocumentJson.Options),
      ImportStatus.Invalid => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "The settings document is not valid. Nothing changed.",
        detail: string.Join(" ", outcome.Errors),
        extensions: new Dictionary<string, object?> { ["result"] = "invalid", ["errors"] = outcome.Errors }),
      _ => Results.Problem(
        statusCode: StatusCodes.Status500InternalServerError,
        title: "A write failed. The import applied part of the document. Send the same document again to finish.",
        detail: outcome.Report?.Failed?.Reason,
        extensions: new Dictionary<string, object?> { ["result"] = "partial_import", ["report"] = outcome.Report })
    };
  }

  // Reads the body with a size cap. Returns null when the body is larger than the limit.
  private static async Task<string?> ReadBodyAsync(HttpRequest http, CancellationToken ct) {
    if (http.ContentLength > MaxRequestBytes) {
      return null;
    }

    var limit = http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (limit is { IsReadOnly: false }) {
      limit.MaxRequestBodySize = MaxRequestBytes;
    }

    using var buffer = new MemoryStream();
    var chunk = new byte[81920];
    int read;
    while ((read = await http.Body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0) {
      if (buffer.Length + read > MaxRequestBytes) {
        return null;
      }

      await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
    }

    return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
  }

  private static IResult TooLarge() =>
    Results.Problem(
      statusCode: StatusCodes.Status413PayloadTooLarge,
      title: "The request is larger than 20 MB. Nothing changed.",
      extensions: new Dictionary<string, object?> { ["result"] = "too_large" });
}
