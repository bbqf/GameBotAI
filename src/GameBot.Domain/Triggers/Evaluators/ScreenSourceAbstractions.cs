using System.Drawing;
using System.Runtime.Versioning;

namespace GameBot.Domain.Triggers.Evaluators;

public interface IScreenSource {
  Bitmap? GetLatestScreenshot();
}

[SupportedOSPlatform("windows")]
public sealed class SingleBitmapScreenSource : IScreenSource {
  private readonly Func<Bitmap?> _provider;
  public SingleBitmapScreenSource(Func<Bitmap?> provider) { _provider = provider; }
  public Bitmap? GetLatestScreenshot() => _provider();
}
