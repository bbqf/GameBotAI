using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.UnitTests.Gamepacks;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.IntegrationTests.Gamepacks;

/// <summary>
/// Real stores in a temporary folder with the real catalog service on top (feature 147). The signature policy store and the
/// catalog settings store share the data root of the kit, so a state snapshot covers both.
/// </summary>
internal sealed class CatalogRig : IDisposable {
  public CatalogRig() {
    Kit = new GamepackTestKit();
    Options = new CatalogOptions();
    Settings = new GamepackCatalogSettingsStore(Kit.Root);
    SignatureSettings = new GamepackSignatureSettingsStore(Kit.Root);
    var checker = new GamepackSignatureChecker(SignatureSettings, new GamepackSignatureVerifier());
    Gamepacks = new GamepackService(Kit.Sequences, Kit.Commands, Kit.Images, Kit.Store, Kit.Validator, Kit.Applier, signatureChecker: checker);
    Factory = new GamepackCatalogSourceFactory(
      Settings,
      () => new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan },
      Options);
    Service = new GamepackCatalogService(Factory, Gamepacks, Options);
  }

  public GamepackTestKit Kit { get; }

  public CatalogOptions Options { get; }

  public GamepackCatalogSettingsStore Settings { get; }

  public GamepackSignatureSettingsStore SignatureSettings { get; }

  public GamepackService Gamepacks { get; }

  public GamepackCatalogSourceFactory Factory { get; }

  public GamepackCatalogService Service { get; }

  public string CatalogFolder => Path.GetFullPath(Path.Combine(Kit.Root, "..", "catalog-" + Path.GetFileName(Kit.Root)));

  public void Dispose() {
    Gamepacks.Dispose();
    Kit.Dispose();
    var folder = Path.GetFullPath(CatalogFolder);
    if (Directory.Exists(folder)) {
      Directory.Delete(folder, recursive: true);
    }
  }
}
