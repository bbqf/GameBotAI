using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using GameBot.Domain.Images;
using GameBot.Domain.Triggers.Evaluators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameBot.Service.Endpoints;

/// <summary>Request body for <c>PUT /api/images/{id}/alternates</c> (feature 097).</summary>
internal sealed class SetImageAlternatesRequest {
  /// <summary>Alternate image ids in match-preference order. An empty list clears the alternates.</summary>
  [JsonPropertyName("alternates")]
  public List<string>? Alternates { get; set; }
}

/// <summary>One entry of an image's alternates list.</summary>
internal sealed class ImageAlternateEntry {
  /// <summary>The alternate image id.</summary>
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  /// <summary>False when the alternate image has since been deleted; detection then skips it.</summary>
  [JsonPropertyName("exists")]
  public bool Exists { get; set; }
}

/// <summary>An image's alternates (feature 097).</summary>
internal sealed class ImageAlternatesResponse {
  /// <summary>The primary image id.</summary>
  [JsonPropertyName("id")]
  public string Id { get; set; } = string.Empty;

  /// <summary>Alternates in registered order.</summary>
  [JsonPropertyName("alternates")]
  public List<ImageAlternateEntry> Alternates { get; set; } = new();
}

// Handlers are named methods rather than inline lambdas: the Roslyn taint analyzers (CA3xxx) analyze
// lambdas as part of the containing method, and their cost grows super-linearly with method size.
[SupportedOSPlatform("windows")]
internal static class ImageAlternatesEndpoints {
  internal static readonly string OperationDescription =
    "Alternates are other stored reference images that also count as a match for this image — for example " +
    "night-lit crops of the same daylight art. Every detection that names the image (sequence conditions, " +
    "wait-for-image steps, image-anchored taps, image-match triggers, the readiness gate and " +
    "POST /api/images/detect) scores the image and each alternate with the same threshold and reports the " +
    "best match. At most " + ImageAlternatesValidator.MaxAlternates + " alternates; each must be a stored image, " +
    "not the image itself, and listed once. Alternates are not expanded transitively. PUT replaces the whole " +
    "list; an empty list clears it. POST /api/images/detect-all is unaffected.";

  public static IEndpointRouteBuilder MapImageAlternatesEndpoints(this IEndpointRouteBuilder app) {
    ArgumentNullException.ThrowIfNull(app);

    app.MapGet($"{ApiRoutes.Images}/{{id}}/alternates", GetAlternatesAsync)
      .WithName("GetImageAlternates")
      .WithTags("Images")
      .WithSummary("List an image's alternate reference images")
      .WithDescription(OperationDescription)
      .Produces<ImageAlternatesResponse>(StatusCodes.Status200OK)
      .Produces(StatusCodes.Status400BadRequest)
      .Produces(StatusCodes.Status404NotFound);

    app.MapPut($"{ApiRoutes.Images}/{{id}}/alternates", SetAlternatesAsync)
      .WithName("SetImageAlternates")
      .WithTags("Images")
      .WithSummary("Replace an image's alternate reference images")
      .WithDescription(OperationDescription)
      .Accepts<SetImageAlternatesRequest>("application/json")
      .Produces<ImageAlternatesResponse>(StatusCodes.Status200OK)
      .Produces(StatusCodes.Status400BadRequest)
      .Produces(StatusCodes.Status404NotFound);

    return app;
  }

  private static IResult InvalidId() =>
    Results.BadRequest(new { error = new { code = "invalid_id", message = "id must be alphanumeric/dash/underscore (1-128 chars)", hint = (string?)null } });

  private static IResult NotFound() =>
    Results.NotFound(new { error = new { code = "not_found", message = "Image not found" } });

  private static async Task<IResult> GetAlternatesAsync(string id, IImageRepository images, IImageAlternatesRepository alternates) {
    if (!ReferenceImageIdValidator.IsValid(id)) return InvalidId();
    if (!await images.ExistsAsync(id).ConfigureAwait(false)) return NotFound();
    return Results.Ok(await BuildResponseAsync(id, alternates.GetAlternates(id), images).ConfigureAwait(false));
  }

  private static async Task<IResult> SetAlternatesAsync(string id, HttpRequest http, IImageRepository images, IImageAlternatesRepository alternates) {
    if (!ReferenceImageIdValidator.IsValid(id)) return InvalidId();
    if (!await images.ExistsAsync(id).ConfigureAwait(false)) return NotFound();

    SetImageAlternatesRequest? req;
    try {
      req = await http.ReadFromJsonAsync<SetImageAlternatesRequest>(http.HttpContext.RequestAborted).ConfigureAwait(false);
    }
    catch (System.Text.Json.JsonException) {
      req = null;
    }
    if (req?.Alternates is null) {
      return Results.BadRequest(new { error = new { code = "invalid_request", message = "alternates is required", hint = "Send { \"alternates\": [ ... ] }; an empty list clears them." } });
    }

    var stored = new HashSet<string>(await images.ListIdsAsync().ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);
    var validation = ImageAlternatesValidator.Validate(id, req.Alternates, stored.Contains);
    if (!validation.IsValid) {
      return Results.BadRequest(new { error = new { code = "invalid_alternates", message = validation.Message, hint = validation.Hint, ids = validation.Ids } });
    }

    alternates.SetAlternates(id, req.Alternates);
    return Results.Ok(await BuildResponseAsync(id, req.Alternates, images).ConfigureAwait(false));
  }

  private static async Task<ImageAlternatesResponse> BuildResponseAsync(string id, IReadOnlyList<string> ids, IImageRepository images) {
    var response = new ImageAlternatesResponse { Id = id };
    foreach (var altId in ids) {
      response.Alternates.Add(new ImageAlternateEntry { Id = altId, Exists = await images.ExistsAsync(altId).ConfigureAwait(false) });
    }
    return response;
  }
}
