using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class PayloadValidationTests {
  [Fact]
  public void PackagePayloadScriptCreatesServiceAndWebUiPayloadStructure() {
    var repoRoot = FindRepoRoot();
    var scriptPath = Path.Combine(repoRoot, "scripts", "package-installer-payload.ps1");

    File.Exists(scriptPath).Should().BeTrue();

    var script = File.ReadAllText(scriptPath);
    script.Should().Contain("installer/wix/payload");
    script.Should().Contain("service");
    script.Should().Contain("web-ui");
    script.Should().Contain("payload-manifest.json");
  }

  [Fact]
  public void PayloadCopiesTheWholeWebUiBuildIncludingTheWebManifest() {
    var repoRoot = FindRepoRoot();
    var script = File.ReadAllText(Path.Combine(repoRoot, "scripts", "package-installer-payload.ps1"));

    // The script copies the full dist folder, so the files from public/ reach the payload.
    script.Should().Contain("Copy-Item -Path (Join-Path $webUiDist \"*\")");
    script.Should().Contain("-Recurse");

    // The manifest and its icons are in public/, which Vite copies into dist/.
    var publicRoot = Path.Combine(repoRoot, "src", "web-ui", "public");
    File.Exists(Path.Combine(publicRoot, "manifest.webmanifest")).Should().BeTrue();
    File.Exists(Path.Combine(publicRoot, "icons", "icon-192.png")).Should().BeTrue();
    File.Exists(Path.Combine(publicRoot, "icons", "icon-512.png")).Should().BeTrue();
    File.Exists(Path.Combine(publicRoot, "icons", "icon-maskable-512.png")).Should().BeTrue();
    File.Exists(Path.Combine(publicRoot, "icons", "apple-touch-icon.png")).Should().BeTrue();
  }

  private static string FindRepoRoot() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null) {
      if (File.Exists(Path.Combine(dir.FullName, "GameBot.sln"))) {
        return dir.FullName;
      }

      dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root containing GameBot.sln");
  }
}
