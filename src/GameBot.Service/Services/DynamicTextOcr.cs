using GameBot.Domain.Profiles.Evaluators;
using GameBot.Service.Services;

namespace GameBot.Service.Services;

internal sealed class DynamicTextOcr : ITextOcr
{
    private readonly IConfigSnapshotService _config;
    private ITextOcr _inner;
    private string _currentMode = "env"; // "env" or "tess"
    private string _currentExe = "tesseract";
    private string _currentLang = "eng";
    private string? _currentPsm;
    private string? _currentOem;
    private readonly object _gate = new();

    public DynamicTextOcr(IConfigSnapshotService config)
    {
        _config = config;
        _inner = new EnvTextOcr();
    }

    private void EnsureInner()
    {
        var snap = _config.Current;
        var useTess = false;
        var exe = "tesseract";
        var lang = "eng";
        string? psm = null;
        string? oem = null;

        if (snap is not null)
        {
            if (snap.Parameters.TryGetValue("GAMEBOT_TESSERACT_ENABLED", out var p) && p.Value is not null)
            {
                var s = p.Value.ToString();
                if (bool.TryParse(s, out var b)) useTess = b; else if (int.TryParse(s, out var i)) useTess = i != 0;
            }
            if (snap.Parameters.TryGetValue("GAMEBOT_TESSERACT_PATH", out var pe) && pe.Value is not null)
            {
                exe = pe.Value.ToString() ?? exe;
            }
            if (snap.Parameters.TryGetValue("GAMEBOT_TESSERACT_LANG", out var pl) && pl.Value is not null)
            {
                lang = pl.Value.ToString() ?? lang;
            }
            if (snap.Parameters.TryGetValue("GAMEBOT_TESSERACT_PSM", out var pp) && pp.Value is not null)
            {
                var s = pp.Value.ToString();
                psm = string.IsNullOrWhiteSpace(s) ? null : s;
            }
            if (snap.Parameters.TryGetValue("GAMEBOT_TESSERACT_OEM", out var po) && po.Value is not null)
            {
                var s = po.Value.ToString();
                oem = string.IsNullOrWhiteSpace(s) ? null : s;
            }
        }

        var newMode = useTess ? "tess" : "env";
        if (newMode != _currentMode || (useTess && (exe != _currentExe || lang != _currentLang || psm != _currentPsm || oem != _currentOem)))
        {
            lock (_gate)
            {
                if (newMode != _currentMode || (useTess && (exe != _currentExe || lang != _currentLang || psm != _currentPsm || oem != _currentOem)))
                {
                    _inner = useTess ? new TesseractProcessOcr(exe, lang, psm, oem) : new EnvTextOcr();
                    _currentMode = newMode;
                    _currentExe = exe;
                    _currentLang = lang;
                    _currentPsm = psm;
                    _currentOem = oem;
                }
            }
        }
    }

    public OcrResult Recognize(System.Drawing.Bitmap image)
    {
        EnsureInner();
        return _inner.Recognize(image);
    }

    public OcrResult Recognize(System.Drawing.Bitmap image, string? language)
    {
        EnsureInner();
        return _inner.Recognize(image, language);
    }
}
