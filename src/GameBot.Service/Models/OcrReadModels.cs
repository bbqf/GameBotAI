namespace GameBot.Service.Models;

/// <summary>Request body of <c>POST /api/ocr/read</c>. Set exactly one of <see cref="Serial"/> and <see cref="CaptureId"/>.</summary>
internal sealed class OcrReadRequest {
  public string? Serial { get; set; }
  public string? CaptureId { get; set; }
  public OcrRegionDto? Region { get; set; }
  public string? Parser { get; set; }
}

/// <summary>A region of the frame, in pixels.</summary>
internal sealed class OcrRegionDto {
  public int X { get; set; }
  public int Y { get; set; }
  public int Width { get; set; }
  public int Height { get; set; }
}

/// <summary>A duration that a parser read from the text.</summary>
internal sealed class OcrParsedValue {
  public string Value { get; set; } = string.Empty;
  public double TotalSeconds { get; set; }
}

/// <summary>Answer (200) of <c>POST /api/ocr/read</c>.</summary>
internal sealed class OcrReadResponse {
  public string Text { get; set; } = string.Empty;
  public double Confidence { get; set; }
  public string Source { get; set; } = string.Empty;
  public int FrameWidth { get; set; }
  public int FrameHeight { get; set; }
  public string? Parser { get; set; }
  public OcrParsedValue? Parsed { get; set; }
  public string? ParseFailureReason { get; set; }
}

/// <summary>Error body of <c>POST /api/ocr/read</c>.</summary>
internal sealed class OcrErrorResponse {
  public string Code { get; set; } = string.Empty;
  public string Message { get; set; } = string.Empty;
}
