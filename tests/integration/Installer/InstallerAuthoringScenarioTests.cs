using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public sealed class InstallerAuthoringScenarioTests
{
  [Fact]
  public void FreshInstallFlowShowsVersionThenConfigurationPagesAndCreatesShortcuts()
  {
    var repoRoot = FindRepoRoot();
    var bundlePath = Path.Combine(repoRoot, "installer", "wix", "Bundle.wxs");
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");
    var networkUiPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "NetworkConfigUi.wxs");
    var directoriesPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "Directories.wxs");
    var configPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "ConfigTemplates.wxs");

    var bundle = File.ReadAllText(bundlePath);
    var product = File.ReadAllText(productPath);
    var networkUi = File.ReadAllText(networkUiPath);
    var directories = File.ReadAllText(directoriesPath);
    var config = File.ReadAllText(configPath);

    bundle.Should().Contain("<bal:WixStandardBootstrapperApplication");
    bundle.Should().Contain("Theme=\"rtfLargeLicense\"");
    bundle.Should().Contain("LicenseFile=\"Assets\\License.generated.rtf\"");
    bundle.Should().Contain("ShowVersion=\"yes\"");

    product.Should().Contain("Value=\"InstallDirDlg\"");
    product.Should().Contain("Condition=\"NOT Installed AND NOT WIX_UPGRADE_DETECTED AND PERSISTED_PORT = &quot;&quot; AND PERSISTED_BIND_HOST = &quot;&quot;\"");

    networkUi.Should().Contain("Publish Dialog=\"InstallDirDlg\" Control=\"Next\" Event=\"NewDialog\" Value=\"NetworkConfigDlg\"");
    networkUi.Should().Contain("Control Id=\"BindHostCombo\"");
    networkUi.Should().Contain("Property=\"PORT\"");
    networkUi.Should().Contain("localhost (127.0.0.1)");

    product.Should().Contain("WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT");
    product.Should().Contain("WIXUI_EXITDIALOGOPTIONALCHECKBOX\" Value=\"1\"");
    product.Should().Contain("Condition=\"WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 AND ((NOT Installed) OR (REINSTALL AND NOT REMOVE~=&quot;ALL&quot;))\"");

    directories.Should().Contain("Id=\"GameBotStartMenuShortcut\"");
    directories.Should().Contain("Id=\"GameBotBackgroundManualStartShortcut\"");
    directories.Should().Contain("url.dll,FileProtocolHandler http://localhost:[PORT]/");

    config.Should().Contain("Name=\"BindHost\"");
    config.Should().Contain("Name=\"Port\"");
  }

  [Fact]
  public void UpgradeFlowSkipsConfigurationPagesAndPreservesExistingSettings()
  {
    var repoRoot = FindRepoRoot();
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");
    var detectionPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "PortDetection.wxs");

    var product = File.ReadAllText(productPath);
    var detection = File.ReadAllText(detectionPath);

    product.Should().Contain("Value=\"VerifyReadyDlg\"");
    product.Should().Contain("Condition=\"NOT Installed AND (WIX_UPGRADE_DETECTED OR PERSISTED_PORT &lt;&gt; &quot;&quot; OR PERSISTED_BIND_HOST &lt;&gt; &quot;&quot;)\"");
    product.Should().Contain("Value=\"InstallDirDlg\"");
    product.Should().Contain("Condition=\"NOT Installed AND NOT WIX_UPGRADE_DETECTED AND PERSISTED_PORT = &quot;&quot; AND PERSISTED_BIND_HOST = &quot;&quot;\"");

    detection.Should().Contain("Condition=\"NOT Installed AND NOT WIX_UPGRADE_DETECTED AND PERSISTED_PORT = &quot;&quot; AND PERSISTED_BIND_HOST = &quot;&quot; AND UILevel &gt;= 5\"");
    detection.Should().Contain("Condition=\"NOT Installed AND NOT WIX_UPGRADE_DETECTED AND PERSISTED_PORT = &quot;&quot; AND PERSISTED_BIND_HOST = &quot;&quot; AND UILevel &lt; 5\"");
    detection.Should().Contain("NormalizeStartMenuShortcut");
    detection.Should().Contain("Condition=\"NOT Installed AND NOT WIX_UPGRADE_DETECTED AND NOT REMOVE~=&quot;ALL&quot;\"");
  }

  [Fact]
  public void RemovalFlowAvoidsConfigurationPagesAndDoesNotStartBackgroundApp()
  {
    var repoRoot = FindRepoRoot();
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");

    var product = File.ReadAllText(productPath);

    product.Should().Contain("Condition=\"WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 AND ((NOT Installed) OR (REINSTALL AND NOT REMOVE~=&quot;ALL&quot;))\"");
    product.Should().Contain("Condition=\"NOT Installed AND NOT WIX_UPGRADE_DETECTED AND PERSISTED_PORT = &quot;&quot; AND PERSISTED_BIND_HOST = &quot;&quot;\"");
    product.Should().Contain("Condition=\"NOT Installed AND (WIX_UPGRADE_DETECTED OR PERSISTED_PORT &lt;&gt; &quot;&quot; OR PERSISTED_BIND_HOST &lt;&gt; &quot;&quot;)\"");
  }

  [Fact]
  public void DowngradeFlowIsBlockedWithExplicitUserMessage()
  {
    var repoRoot = FindRepoRoot();
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");
    var bundlePath = Path.Combine(repoRoot, "installer", "wix", "Bundle.wxs");

    var product = File.ReadAllText(productPath);
    var bundle = File.ReadAllText(bundlePath);

    product.Should().Contain("<MajorUpgrade");
    product.Should().Contain("DowngradeErrorMessage=\"A newer version of GameBot is already installed.\"");
    bundle.Should().Contain("SuppressDowngradeFailure=\"no\"");
  }

  private static string FindRepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
      if (File.Exists(Path.Combine(dir.FullName, "GameBot.sln")))
      {
        return dir.FullName;
      }

      dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root containing GameBot.sln");
  }
}
