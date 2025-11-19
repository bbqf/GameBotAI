using System.Drawing;

namespace GameBot.Domain.Triggers.Evaluators;

// Environment-driven OCR stub used in tests. Reads variables at call time so tests can
// set them after constructing the instance:
//   GAMEBOT_TEST_OCR_TEXT  -> returned OCR text
//   GAMEBOT_TEST_OCR_CONF  -> confidence (double)
// Fallbacks:
//   GAMEBOT_OCR_STATIC_TEXT -> text when test-specific var not set
// If confidence var missing or invalid and text exists, defaults to 1.0.
public sealed class EnvTextOcr : ITextOcr {
  public OcrResult Recognize(Bitmap image) => Recognize(image, null);
  public OcrResult Recognize(Bitmap image, string? language) {
    var text = Environment.GetEnvironmentVariable("GAMEBOT_TEST_OCR_TEXT")
               ?? Environment.GetEnvironmentVariable("GAMEBOT_OCR_STATIC_TEXT")
               ?? string.Empty;
    var confRaw = Environment.GetEnvironmentVariable("GAMEBOT_TEST_OCR_CONF");
    double conf = 0.0;
    if (!string.IsNullOrWhiteSpace(confRaw)) {
      bool parsed = double.TryParse(confRaw, out conf);
      if (!parsed) {
        conf = 0.0; // retain default if parse fails
      }
    }
    if (conf <= 0 && text.Length > 0) {
      conf = 1.0; // default high confidence when text provided without explicit value
    }
    return new OcrResult(text, conf);
  }
}
