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
  }

  internal sealed class DetectResponse {
    [JsonPropertyName("matches")]
    public System.Collections.ObjectModel.Collection<MatchResult> Matches { get; set; } = new();

    [JsonPropertyName("limitsHit")]
    public bool LimitsHit { get; set; }
  }

  internal sealed class MatchResult {
    [JsonPropertyName("templateId")]
    public string TemplateId { get; set; } = string.Empty;

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
