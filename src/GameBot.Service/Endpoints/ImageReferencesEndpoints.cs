using System.ComponentModel.DataAnnotations;
using System.Drawing;
using System.IO;
using System.Text.Json;
using GameBot.Domain.Triggers.Evaluators;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using System.Runtime.Versioning;

namespace GameBot.Service.Endpoints;

[SupportedOSPlatform("windows")]
internal static class ImageReferencesEndpoints {
  internal sealed class ImageReferenceEndpointComponent { }
  internal sealed class UploadImageRequest {
    [Required] public required string Id { get; set; }
    // Accept base64 PNG/JPEG payload as data URL or raw base64
    [Required] public required string Data { get; set; }
  }

  public static IEndpointRouteBuilder MapImageReferenceEndpoints(this IEndpointRouteBuilder app) {
    ArgumentNullException.ThrowIfNull(app);

    app.MapGet("/api/images", () => {
      var dataRoot = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")
                     ?? Path.Combine(AppContext.BaseDirectory, "data");
      var imagesDir = Path.Combine(dataRoot, "images");
      Directory.CreateDirectory(imagesDir);
      var ids = Directory.GetFiles(imagesDir, "*.png")
                         .Select(fn => Path.GetFileNameWithoutExtension(fn))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Select(id => new { id });
      return Results.Ok(ids);
    }).WithName("ListImageReferences").WithTags("Images");

    app.MapPost("/api/images", (UploadImageRequest req, IReferenceImageStore store, Microsoft.Extensions.Logging.ILogger<ImageReferenceEndpointComponent> logger) => {
      ArgumentNullException.ThrowIfNull(req);
      ArgumentNullException.ThrowIfNull(store);
      if (string.IsNullOrWhiteSpace(req.Id) || string.IsNullOrWhiteSpace(req.Data))
        {
          logger.LogUploadRejectedMissingFields();
          return Results.BadRequest(new { error = new { code = "invalid_request", message = "id and data are required", hint = (string?)null } });
        }

      try {
        var b64 = req.Data;
        var comma = b64.IndexOf(',', System.StringComparison.Ordinal);
        if (comma >= 0) b64 = b64[(comma + 1)..]; // strip data URL header
        var bytes = Convert.FromBase64String(b64);
        using var ms = new MemoryStream(bytes);
        using var bmp = new Bitmap(ms);
        // Store a clone to decouple from stream lifetime
        // Store reference image (AddOrUpdate to allow overwrite)
        var cloned = (Bitmap)bmp.Clone();
        var overwriting = store.Exists(req.Id);
        store.AddOrUpdate(req.Id, cloned);
        // Ensure disk persistence even if underlying store is in-memory
        try {
          var dataRoot = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")
                         ?? Path.Combine(AppContext.BaseDirectory, "data");
          var imagesDir = Path.Combine(dataRoot, "images");
          Directory.CreateDirectory(imagesDir);
          var targetFile = Path.Combine(imagesDir, req.Id + ".png");
          cloned.Save(targetFile);
        } catch { /* best-effort; store may already persist */ }
        logger.LogImagePersisted(req.Id, bytes.Length, overwriting);
        return Results.Created($"/api/images/{req.Id}", new { id = req.Id, overwrite = overwriting });
      }
      catch (Exception ex) {
        logger.LogUploadFailed(ex, req.Id);
        return Results.BadRequest(new { error = new { code = "invalid_image", message = "Failed to parse image", hint = ex.Message } });
      }
    }).WithName("UploadImageReference").WithTags("Images");

    app.MapGet("/api/images/{id}", (string id, IReferenceImageStore store, Microsoft.Extensions.Logging.ILogger<ImageReferenceEndpointComponent> logger) => {
      ArgumentNullException.ThrowIfNull(id);
      ArgumentNullException.ThrowIfNull(store);
      if (store.Exists(id)) {
        logger.LogImageResolvedFromStore(id);
        return Results.Ok(new { id });
      }
      var dataRoot = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR")
                     ?? Path.Combine(AppContext.BaseDirectory, "data");
      var physical = Path.Combine(dataRoot, "images", id + ".png");
      if (File.Exists(physical)) {
        logger.LogImageResolvedFromDiskFallback(id);
        return Results.Ok(new { id, fallback = true });
      }
      logger.LogImageNotFound(id);
      return Results.NotFound(new { error = new { code = "not_found", message = "Image not found" } });
    }).WithName("GetImageReference").WithTags("Images");

    app.MapDelete("/api/images/{id}", (string id, IReferenceImageStore store, Microsoft.Extensions.Logging.ILogger<ImageReferenceEndpointComponent> logger) => {
      ArgumentNullException.ThrowIfNull(id);
      ArgumentNullException.ThrowIfNull(store);
      if (store.Delete(id)) { logger.LogImageDeleted(id); return Results.NoContent(); }
      logger.LogDeleteRequestMissing(id);
      return Results.NotFound(new { error = new { code = "not_found", message = "Image not found" } });
    }).WithName("DeleteImageReference").WithTags("Images");

    return app;
  }
}
