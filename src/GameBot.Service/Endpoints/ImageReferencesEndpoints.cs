using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using GameBot.Domain.Images;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace GameBot.Service.Endpoints;

[SupportedOSPlatform("windows")]
internal static class ImageReferencesEndpoints {
  internal sealed class ImageReferenceEndpointComponent { }
  internal sealed class UploadImageRequest {
    [Required] public string? Id { get; set; }
    public string? Data { get; set; }
    public IFormFile? File { get; set; }
  }

  internal sealed class OverwriteImageRequest {
    public string? Data { get; set; }
    public IFormFile? File { get; set; }
  }

  private static string SanitizeForLog(string? value)
  {
    if (string.IsNullOrEmpty(value)) return string.Empty;
    return value.Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
  }

  public static IEndpointRouteBuilder MapImageReferenceEndpoints(this IEndpointRouteBuilder app) {
    ArgumentNullException.ThrowIfNull(app);

    app.MapGet(ApiRoutes.Images, async (IImageRepository repo) => {
      var ids = await repo.ListIdsAsync().ConfigureAwait(false);
      var ordered = ids.OrderBy(i => i, StringComparer.OrdinalIgnoreCase).ToArray();
      return Results.Ok(new { ids = ordered });
    }).WithName("ListImageReferences").WithTags("Images");

    app.MapPost(ApiRoutes.Images, async (HttpRequest http, IImageRepository repo, Microsoft.Extensions.Logging.ILogger<ImageReferenceEndpointComponent> logger) => {
      UploadImageRequest? req;
      if (http.HasFormContentType)
      {
        req = new UploadImageRequest
        {
          Id = http.Form["id"],
          File = http.Form.Files.GetFile("file")
        };
      }
      else
      {
        req = await http.ReadFromJsonAsync<UploadImageRequest>().ConfigureAwait(false);
      }

      if (req is null || string.IsNullOrWhiteSpace(req.Id) || (req.File is null && string.IsNullOrWhiteSpace(req.Data))) {
        logger.LogUploadRejectedMissingFields();
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "id and file/data are required", hint = (string?)null } });
      }

      if (!ReferenceImageIdValidator.IsValid(req.Id)) {
        return Results.BadRequest(new { error = new { code = "invalid_id", message = "id must be alphanumeric/dash/underscore (1-128 chars)", hint = (string?)null } });
      }

      byte[] bytes;
      string contentType;
      string? filename = null;

      if (req.File is not null)
      {
        if (!ImageDetectionsValidation.ValidateContentType(req.File.ContentType)) {
          return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }
        if (!ImageDetectionsValidation.ValidateContentLength(req.File.Length)) {
          return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        using var ms = new MemoryStream();
        using (var fileStream = req.File.OpenReadStream())
        {
          await fileStream.CopyToAsync(ms, http.HttpContext.RequestAborted).ConfigureAwait(false);
        }
        bytes = ms.ToArray();
        contentType = string.IsNullOrWhiteSpace(req.File.ContentType) ? "image/png" : req.File.ContentType!;
        filename = req.File.FileName;
      }
      else
      {
        try
        {
          var b64 = req.Data!;
          var comma = b64.IndexOf(',', StringComparison.Ordinal);
          if (comma >= 0) b64 = b64[(comma + 1)..];
          bytes = Convert.FromBase64String(b64);
        }
        catch (FormatException ex)
        {
          logger.LogUploadFailed(ex, SanitizeForLog(req.Id));
          return Results.BadRequest(new { error = new { code = "invalid_image", message = "Failed to parse image", hint = ex.Message } });
        }

        if (bytes.Length == 0 || bytes.Length > ImageDetectionsValidation.MaxImageBytes)
        {
          return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        contentType = "image/png";
      }

      var safeId = SanitizeForLog(req.Id);
      try {
        var exists = await repo.ExistsAsync(req.Id).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false);
        var saved = await repo.SaveAsync(req.Id, stream, contentType, filename, overwrite: true).ConfigureAwait(false);
        logger.LogImagePersisted(safeId, (int)saved.SizeBytes, exists);
        return Results.Created($"{ApiRoutes.Images}/{req.Id}", new { id = saved.Id, contentType = saved.ContentType, sizeBytes = saved.SizeBytes, createdAtUtc = saved.CreatedAtUtc, updatedAtUtc = saved.UpdatedAtUtc, overwrite = exists });
      }
      catch (Exception ex) {
        logger.LogUploadFailed(ex, safeId);
        return Results.BadRequest(new { error = new { code = "invalid_image", message = "Failed to save image", hint = ex.Message } });
      }
    }).Accepts<UploadImageRequest>("application/json").WithName("UploadImageReference").WithTags("Images");

    app.MapGet($"{ApiRoutes.Images}/{{id}}", async (string id, IImageRepository repo, Microsoft.Extensions.Logging.ILogger<ImageReferenceEndpointComponent> logger) => {
      ArgumentNullException.ThrowIfNull(id);
      var safeId = SanitizeForLog(id);
      var meta = await repo.GetAsync(id).ConfigureAwait(false);
      if (meta is null) {
        logger.LogImageNotFound(safeId);
        return Results.NotFound(new { error = new { code = "not_found", message = "Image not found" } });
      }

      var stream = await repo.OpenReadAsync(id).ConfigureAwait(false);
      if (stream is null) {
        logger.LogImageNotFound(safeId);
        return Results.NotFound(new { error = new { code = "not_found", message = "Image not found" } });
      }

      logger.LogImageResolvedFromStore(safeId);
      return Results.Stream(stream, contentType: meta.ContentType, lastModified: meta.UpdatedAtUtc, enableRangeProcessing: true);
    }).WithName("GetImageReference").WithTags("Images");

    app.MapGet($"{ApiRoutes.Images}/{{id}}/metadata", async (string id, IImageRepository repo) => {
      var meta = await repo.GetAsync(id).ConfigureAwait(false);
      if (meta is null) return Results.NotFound(new { error = new { code = "not_found", message = "Image not found" } });
      return Results.Ok(new { id = meta.Id, contentType = meta.ContentType, sizeBytes = meta.SizeBytes, filename = meta.Filename, createdAtUtc = meta.CreatedAtUtc, updatedAtUtc = meta.UpdatedAtUtc });
    }).WithName("GetImageMetadata").WithTags("Images");

    app.MapPut($"{ApiRoutes.Images}/{{id}}", async (string id, HttpRequest http, IImageRepository repo, Microsoft.Extensions.Logging.ILogger<ImageReferenceEndpointComponent> logger) => {
      OverwriteImageRequest? req;
      if (http.HasFormContentType)
      {
        req = new OverwriteImageRequest
        {
          File = http.Form.Files.GetFile("file")
        };
      }
      else
      {
        req = await http.ReadFromJsonAsync<OverwriteImageRequest>().ConfigureAwait(false);
      }

      ArgumentNullException.ThrowIfNull(id);
      if (req is null || (req.File is null && string.IsNullOrWhiteSpace(req.Data))) {
        logger.LogUploadRejectedMissingFields();
        return Results.BadRequest(new { error = new { code = "invalid_request", message = "file or data is required", hint = (string?)null } });
      }

      if (!ReferenceImageIdValidator.IsValid(id)) {
        return Results.BadRequest(new { error = new { code = "invalid_id", message = "id must be alphanumeric/dash/underscore (1-128 chars)", hint = (string?)null } });
      }

      byte[] bytes;
      string contentType;
      string? filename = null;

      if (req.File is not null)
      {
        if (!ImageDetectionsValidation.ValidateContentType(req.File.ContentType)) {
          return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }
        if (!ImageDetectionsValidation.ValidateContentLength(req.File.Length)) {
          return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        using var ms = new MemoryStream();
        using (var fileStream = req.File.OpenReadStream())
        {
          await fileStream.CopyToAsync(ms, http.HttpContext.RequestAborted).ConfigureAwait(false);
        }
        bytes = ms.ToArray();
        contentType = string.IsNullOrWhiteSpace(req.File.ContentType) ? "image/png" : req.File.ContentType!;
        filename = req.File.FileName;
      }
      else
      {
        try
        {
          var b64 = req.Data!;
          var comma = b64.IndexOf(',', StringComparison.Ordinal);
          if (comma >= 0) b64 = b64[(comma + 1)..];
          bytes = Convert.FromBase64String(b64);
        }
        catch (FormatException ex)
        {
          logger.LogUploadFailed(ex, SanitizeForLog(id));
          return Results.BadRequest(new { error = new { code = "invalid_image", message = "Failed to parse image", hint = ex.Message } });
        }

        if (bytes.Length == 0 || bytes.Length > ImageDetectionsValidation.MaxImageBytes)
        {
          return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        contentType = "image/png";
      }

      var exists = await repo.ExistsAsync(id).ConfigureAwait(false);
      if (!exists) {
        return Results.NotFound(new { error = new { code = "not_found", message = "Image not found" } });
      }

      var safeId = SanitizeForLog(id);
      try {
        using var stream = new MemoryStream(bytes, writable: false);
        var saved = await repo.SaveAsync(id, stream, contentType, filename, overwrite: true).ConfigureAwait(false);
        logger.LogImagePersisted(safeId, (int)saved.SizeBytes, true);
        return Results.Ok(new { id = saved.Id, contentType = saved.ContentType, sizeBytes = saved.SizeBytes, updatedAtUtc = saved.UpdatedAtUtc });
      }
      catch (Exception ex) {
        logger.LogUploadFailed(ex, safeId);
        return Results.BadRequest(new { error = new { code = "invalid_image", message = "Failed to save image", hint = ex.Message } });
      }
    }).Accepts<OverwriteImageRequest>("application/json").WithName("OverwriteImageReference").WithTags("Images");

    app.MapDelete($"{ApiRoutes.Images}/{{id}}", async (string id, IImageRepository repo, IImageReferenceRepository refs, Microsoft.Extensions.Logging.ILogger<ImageReferenceEndpointComponent> logger) => {
      ArgumentNullException.ThrowIfNull(id);
      var safeId = SanitizeForLog(id);
      if (!ReferenceImageIdValidator.IsValid(id)) {
        return Results.BadRequest(new { error = new { code = "invalid_id", message = "id must be alphanumeric/dash/underscore (1-128 chars)", hint = (string?)null } });
      }

      if (!await repo.ExistsAsync(id).ConfigureAwait(false)) {
        logger.LogImageNotFound(safeId);
        return Results.NotFound(new { error = new { code = "not_found", message = "Image not found" } });
      }

      var blocking = await refs.FindReferencingTriggerIdsAsync(id).ConfigureAwait(false);
      if (blocking.Count > 0) {
        return Results.Conflict(new { blockingTriggerIds = blocking });
      }

      var deleted = await repo.DeleteAsync(id).ConfigureAwait(false);
      if (!deleted) {
        logger.LogDeleteRequestMissing(safeId);
        return Results.NotFound(new { error = new { code = "not_found", message = "Image not found" } });
      }

      logger.LogImageDeleted(safeId);
      return Results.NoContent();
    }).WithName("DeleteImageReference").WithTags("Images");

    return app;
  }
}
