using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameBot.Service.Endpoints.Dto {
  internal sealed class DetectAllRequest {
    [JsonPropertyName("captureId")]
    public string? CaptureId { get; set; }
  }

  internal sealed class DetectAllMatch {
    [JsonPropertyName("imageId")]
    public string ImageId { get; set; } = string.Empty;

    [JsonPropertyName("imageName")]
    public string ImageName { get; set; } = string.Empty;

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
  }

  internal sealed class DetectAllResponse {
    [JsonPropertyName("matches")]
    public System.Collections.ObjectModel.Collection<DetectAllMatch> Matches { get; set; } = new();
  }
  internal sealed class DetectRequest {
    [JsonPropertyName("referenceImageId")]
    public string? ReferenceImageId { get; set; }

    [JsonPropertyName("threshold")]
    public double? Threshold { get; set; }

    [JsonPropertyName("maxResults")]
    public int? MaxResults { get; set; }

    [JsonPropertyName("overlap")]
    public double? Overlap { get; set; }

    /// <summary>
    /// Optional: measure against this previously taken capture instead of whatever screen the
    /// service would otherwise infer. Same identifier <c>/api/images/detect-all</c> accepts, and the
    /// one returned in the <c>X-Capture-Id</c> header of <c>GET /api/emulator/screenshot</c>.
    /// Mutually exclusive with <see cref="SessionId"/>. Blank counts as absent (feature 085).
    /// </summary>
    [JsonPropertyName("captureId")]
    public string? CaptureId { get; set; }

    /// <summary>
    /// Optional: measure against this session's latest captured frame. Mutually exclusive with
    /// <see cref="CaptureId"/>. Blank counts as absent (feature 085).
    /// </summary>
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
  }

  internal sealed class DetectResponse {
    [JsonPropertyName("matches")]
    public System.Collections.ObjectModel.Collection<MatchResult> Matches { get; set; } = new();

    [JsonPropertyName("limitsHit")]
    public bool LimitsHit { get; set; }

    /// <summary>
    /// True when the reference image carried a transparency mask and only its retained pixels were
    /// compared (feature 089). Additive: no existing field changes shape or meaning.
    /// </summary>
    [JsonPropertyName("masked")]
    public bool Masked { get; set; }

    /// <summary>
    /// How many template pixels the comparison used — the pixels <b>kept</b>, never the pixels
    /// masked out. Zero when <see cref="Masked"/> is false.
    /// </summary>
    [JsonPropertyName("retainedPixelCount")]
    public int RetainedPixelCount { get; set; }
  }

  internal sealed class MatchResult {
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>
    /// Id of the reference that produced this match: <see cref="TemplateId"/> itself or one of its
    /// alternates (feature 097). Additive; <see cref="TemplateId"/> keeps the requested image id.
    /// </summary>
    [JsonPropertyName("matchedReferenceId")]
    public string MatchedReferenceId { get; set; } = string.Empty;

    [JsonPropertyName("score")]
    public double Score { get; set; }

    // Keep backward compatibility (score) and align contract expectations (confidence)
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("bbox")]
    public NormalizedRect Bbox { get; set; } = new();

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }

    [JsonPropertyName("overlap")]
    public double Overlap { get; set; }
  }

  internal sealed class NormalizedRect {
    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("width")]
    public double Width { get; set; }

    [JsonPropertyName("height")]
    public double Height { get; set; }
  }
}
