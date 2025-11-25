using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Models;

namespace GameBot.Service.Services;

[SuppressMessage("Performance", "CA1859:Change type of field '_inner'", Justification = "Interface required for runtime swapping between Env and Tesseract implementations")]
internal sealed class DynamicTextOcr : ITextOcr {
  private readonly IConfigSnapshotService _config;
  private readonly ITesseractInvocationLogger _invocationLogger;
  private readonly ITestOcrProcessRunner? _processRunner;
  private ITextOcr _inner;
  private string _currentMode = "env"; // "env" or "tess"
  private string _currentExe = "tesseract";
  private string _currentLang = "eng";
  private string? _currentPsm;
  private string? _currentOem;
  private readonly object _gate = new();

  public DynamicTextOcr(IConfigSnapshotService config, ITesseractInvocationLogger invocationLogger, IEnumerable<ITestOcrProcessRunner> runners) {
    _config = config;
    _invocationLogger = invocationLogger;
    _processRunner = runners?.FirstOrDefault();
    _inner = new EnvTextOcr();
  }

  private void EnsureInner() {
    var snap = _config.Current;
    var useTess = ParseBoolOrNumber(GetValue("GAMEBOT_TESSERACT_ENABLED", snap));
    var exe = GetValue("GAMEBOT_TESSERACT_PATH", snap) ?? "tesseract";
    var lang = GetValue("GAMEBOT_TESSERACT_LANG", snap) ?? "eng";
    var psm = NormalizeOptional(GetValue("GAMEBOT_TESSERACT_PSM", snap));
    var oem = NormalizeOptional(GetValue("GAMEBOT_TESSERACT_OEM", snap));

    var newMode = useTess ? "tess" : "env";
    if (newMode != _currentMode || (useTess && (exe != _currentExe || lang != _currentLang || psm != _currentPsm || oem != _currentOem))) {
      lock (_gate) {
        if (newMode != _currentMode || (useTess && (exe != _currentExe || lang != _currentLang || psm != _currentPsm || oem != _currentOem))) {
          _inner = useTess ? new TesseractProcessOcr(exe, lang, psm, oem, _invocationLogger, _processRunner) : new EnvTextOcr();
          _currentMode = newMode;
          _currentExe = exe;
          _currentLang = lang;
          _currentPsm = psm;
          _currentOem = oem;
        }
      }
    }
  }

  public OcrResult Recognize(System.Drawing.Bitmap image) {
    EnsureInner();
    return _inner.Recognize(image);
  }

  public OcrResult Recognize(System.Drawing.Bitmap image, string? language) {
    EnsureInner();
    return _inner.Recognize(image, language);
  }

  private static string? GetValue(string key, ConfigurationSnapshot? snapshot) {
    if (snapshot?.Parameters is { } parameters && parameters.TryGetValue(key, out var parameter) && parameter.Value is not null) {
      return parameter.Value.ToString();
    }
    return Environment.GetEnvironmentVariable(key);
  }

  private static bool ParseBoolOrNumber(string? value) {
    if (string.IsNullOrWhiteSpace(value)) {
      return false;
    }
    if (bool.TryParse(value, out var b)) {
      return b;
    }
    if (int.TryParse(value, out var i)) {
      return i != 0;
    }
    return false;
  }

  private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
