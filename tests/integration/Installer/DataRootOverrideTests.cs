using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class DataRootOverrideTests {
  [Fact]
  public void BundleAndValidationFragmentsReferenceDataRootOverrideAndValidation() {
    var repoRoot = FindRepoRoot();
    var bundlePath = Path.Combine(repoRoot, "installer", "wix", "Bundle.wxs");
    var validationPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "Validation.wxs");

    File.ReadAllText(bundlePath).Should().Contain("Variable Name=\"DATA_ROOT\"");
    File.ReadAllText(validationPath).Should().Contain("DATA_ROOT_WRITEABLE");
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
