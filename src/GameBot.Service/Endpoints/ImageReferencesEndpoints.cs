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
internal static class ImageReferencesEndpoints
{
    internal sealed class UploadImageRequest
    {
        [Required] public required string Id { get; set; }
        // Accept base64 PNG/JPEG payload as data URL or raw base64
        [Required] public required string Data { get; set; }
    }

    public static IEndpointRouteBuilder MapImageReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/images", (UploadImageRequest req, IReferenceImageStore store) =>
        {
            ArgumentNullException.ThrowIfNull(req);
            ArgumentNullException.ThrowIfNull(store);
            if (string.IsNullOrWhiteSpace(req.Id) || string.IsNullOrWhiteSpace(req.Data))
                return Results.BadRequest(new { error = new { code = "invalid_request", message = "id and data are required", hint = (string?)null } });

            try
            {
                var b64 = req.Data;
                var comma = b64.IndexOf(',', System.StringComparison.Ordinal);
                if (comma >= 0) b64 = b64[(comma + 1)..]; // strip data URL header
                var bytes = Convert.FromBase64String(b64);
                using var ms = new MemoryStream(bytes);
                using var bmp = new Bitmap(ms);
                // Store a clone to decouple from stream lifetime
                // Store reference image (AddOrUpdate to allow overwrite)
                store.AddOrUpdate(req.Id, (Bitmap)bmp.Clone());
                return Results.Created($"/images/{req.Id}", new { id = req.Id });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = new { code = "invalid_image", message = "Failed to parse image", hint = ex.Message } });
            }
        }).WithName("UploadImageReference");

        app.MapGet("/images/{id}", (string id, IReferenceImageStore store) =>
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(store);
            if ((store as MemoryReferenceImageStore)?.TryGet(id, out var bmp) == true)
            {
                return Results.Ok(new { id });
            }
            return Results.NotFound();
        }).WithName("GetImageReference");

        return app;
    }
}
