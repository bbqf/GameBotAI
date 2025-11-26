using System.Globalization;

namespace GameBot.Domain.Triggers.Evaluators;

internal readonly record struct OcrToken(
  string Text,
  int Left,
  int Top,
  int Width,
  int Height,
  int LineIndex,
  int WordIndex,
  int Confidence);

internal static class TesseractTsvParser {
  private static readonly char[] LineSplit = new[] { '\n', '\r' };

  public static IReadOnlyList<OcrToken> Parse(string? tsv, out double aggregateConfidence, out string? reason) {
    aggregateConfidence = 0;
    reason = null;
    if (string.IsNullOrWhiteSpace(tsv)) {
      reason = "empty_tsv";
      return Array.Empty<OcrToken>();
    }
    var lines = tsv.Split(LineSplit, StringSplitOptions.RemoveEmptyEntries);
    if (lines.Length == 0) { reason = "no_lines"; return Array.Empty<OcrToken>(); }
    // Header validation
    var header = lines[0].Trim();
    if (!header.Contains("conf", StringComparison.Ordinal)) {
      reason = "tsv_format_unexpected";
      return Array.Empty<OcrToken>();
    }
    var tokens = new List<OcrToken>(Math.Max(4, lines.Length - 1));
    int validForAggregation = 0;
    int confidenceSum = 0;
    for (int i = 1; i < lines.Length; i++) {
      var line = lines[i];
      // Expected 12 columns per spec: level page_num block_num par_num line_num word_num left top width height conf text
      var cols = line.Split('\t');
      if (cols.Length < 12) continue; // skip malformed
      // Parse needed columns; tolerate parse failures by skipping row
      if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)) continue; // level
      if (!int.TryParse(cols[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var wordNum)) continue; // word_num
      if (!int.TryParse(cols[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lineNum)) continue; // line_num
      if (!int.TryParse(cols[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)) continue;
      if (!int.TryParse(cols[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var top)) continue;
      if (!int.TryParse(cols[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)) continue;
      if (!int.TryParse(cols[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)) continue;
      if (!int.TryParse(cols[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out var conf)) conf = -1;
      var text = cols[11] ?? string.Empty;
      var trimmed = text.Trim();
      if (level != 5 || trimmed.Length == 0) continue; // only include word-level tokens with text
      var token = new OcrToken(trimmed, left, top, width, height, lineNum, wordNum, conf);
      tokens.Add(token);
      if (conf >= 0 && conf <= 100 && trimmed.Length > 0) {
        confidenceSum += conf;
        validForAggregation++;
      }
    }
    if (validForAggregation == 0) {
      aggregateConfidence = 0;
      reason = tokens.Count == 0 ? "no_tokens" : "no_valid_tokens";
      return tokens;
    }
    aggregateConfidence = confidenceSum / (double)validForAggregation;
    return tokens;
  }
}
