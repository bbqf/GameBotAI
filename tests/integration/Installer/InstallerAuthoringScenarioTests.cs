using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public sealed class InstallerAuthoringScenarioTests {
  private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";

  [Fact]
  public void FreshInstallFlowShowsVersionThenConfigurationPagesAndCreatesShortcuts() {
    var repoRoot = FindRepoRoot();
    var bundlePath = Path.Combine(repoRoot, "installer", "wix", "Bundle.wxs");
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");
    var networkUiPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "NetworkConfigUi.wxs");
    var directoriesPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "Directories.wxs");

    var bundle = File.ReadAllText(bundlePath);
    var product = File.ReadAllText(productPath);
    var networkUi = File.ReadAllText(networkUiPath);
    var directories = File.ReadAllText(directoriesPath);

    bundle.Should().Contain("<bal:WixStandardBootstrapperApplication");
    bundle.Should().Contain("Theme=\"rtfLargeLicense\"");
    bundle.Should().Contain("LicenseFile=\"Assets\\License.generated.rtf\"");
    bundle.Should().Contain("ShowVersion=\"yes\"");

    product.Should().Contain("Value=\"InstallDirDlg\"");

    networkUi.Should().Contain("Publish Dialog=\"InstallDirDlg\" Control=\"Next\" Event=\"NewDialog\" Value=\"NetworkConfigDlg\"");
    networkUi.Should().Contain("Control Id=\"BindHostCombo\"");
    networkUi.Should().Contain("Property=\"PORT\"");
    networkUi.Should().Contain("localhost (127.0.0.1)");

    product.Should().Contain("WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT");
    product.Should().Contain("WIXUI_EXITDIALOGOPTIONALCHECKBOX\" Value=\"1\"");
    product.Should().Contain("Condition=\"WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 AND ((NOT Installed) OR (REINSTALL AND NOT REMOVE~=&quot;ALL&quot;))\"");

    directories.Should().Contain("Id=\"GameBotStartMenuShortcut\"");
    directories.Should().Contain("Id=\"GameBotBackgroundManualStartShortcut\"");
    directories.Should().Contain("url.dll,FileProtocolHandler [SHORTCUT_URL]");
  }

  [Fact]
  public void EveryInteractiveRunShowsTheFolderPageAndTheNetworkPageAlsoOnUpgrade() {
    var product = LoadWix("Product.wxs");

    // No row sends the welcome page to the ready page. This was the upgrade branch that skipped the network page.
    product.Descendants(Wix + "Publish")
      .Where(e => Attr(e, "Dialog") == "WelcomeDlg" && Attr(e, "Control") == "Next")
      .Should().OnlyContain(e => Attr(e, "Value") == "InstallDirDlg");
    product.Descendants(Wix + "Publish")
      .Where(e => Attr(e, "Dialog") == "WelcomeDlg" && Attr(e, "Control") == "Next")
      .Should().OnlyContain(e => !Attr(e, "Condition").Contains("WIX_UPGRADE_DETECTED", StringComparison.Ordinal));

    var welcome = product.Descendants(Wix + "Publish").Single(e => Attr(e, "Dialog") == "WelcomeDlg" && Attr(e, "Control") == "Next");
    Attr(welcome, "Condition").Should().Be("NOT Installed");
  }

  [Fact]
  public void FolderPageResolvesTheBindingBeforeTheNetworkPage() {
    var networkUi = LoadWix("Fragments", "NetworkConfigUi.wxs");
    var folderNext = networkUi.Descendants(Wix + "Publish")
      .Where(e => Attr(e, "Dialog") == "InstallDirDlg" && Attr(e, "Control") == "Next")
      .ToList();

    var resolve = folderNext.Single(e => Attr(e, "Event") == "DoAction" && Attr(e, "Value") == "ResolveNetworkBinding");
    var show = folderNext.Single(e => Attr(e, "Event") == "NewDialog" && Attr(e, "Value") == "NetworkConfigDlg");
    int.Parse(Attr(resolve, "Order"), System.Globalization.CultureInfo.InvariantCulture)
      .Should().BeLessThan(int.Parse(Attr(show, "Order"), System.Globalization.CultureInfo.InvariantCulture));
  }

  [Fact]
  public void NetworkPageValidatesBeforeItLeaves() {
    var networkUi = LoadWix("Fragments", "NetworkConfigUi.wxs");
    var next = networkUi.Descendants(Wix + "Publish")
      .Where(e => Attr(e, "Dialog") == "NetworkConfigDlg" && Attr(e, "Control") == "Next")
      .ToList();

    var validate = next.Single(e => Attr(e, "Event") == "DoAction");
    Attr(validate, "Value").Should().Be("ValidateNetworkBinding");
    var validateOrder = int.Parse(Attr(validate, "Order"), System.Globalization.CultureInfo.InvariantCulture);

    // Every other event of the Next button runs after the check, and only when the check found no error.
    // The wait dialog row must stay always true. A wait dialog stays open until its condition is true, so a
    // condition on the error text left a "Please wait" popup open when the check found an error.
    foreach (var other in next.Where(e => e != validate)) {
      int.Parse(Attr(other, "Order"), System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThan(validateOrder);
      if (Attr(other, "Event") == "SpawnWaitDialog") {
        Attr(other, "Condition").Should().Be("1");
      }
      else if (Attr(other, "Value") == "NetworkBindingErrorDlg") {
        // The error dialog opens only when there is an error. A new dialog formats its text when it opens.
        Attr(other, "Event").Should().Be("SpawnDialog");
        Attr(other, "Condition").Should().Be("NETWORK_BINDING_ERROR <> \"\"");
      }
      else {
        Attr(other, "Condition").Should().Contain("NETWORK_BINDING_ERROR = \"\"");
      }
    }

    // A control on the network page does not redraw when a custom action changes the property. The error dialog does.
    var errorDialog = networkUi.Descendants(Wix + "Dialog").Single(e => Attr(e, "Id") == "NetworkBindingErrorDlg");
    errorDialog.Descendants(Wix + "Control").Single(e => Attr(e, "Id") == "ErrorText")
      .Attribute("Text")!.Value.Should().Be("[NETWORK_BINDING_ERROR]");
    networkUi.Descendants(Wix + "Dialog").Single(e => Attr(e, "Id") == "NetworkConfigDlg")
      .Descendants(Wix + "Control").Should().NotContain(e => Attr(e, "Id") == "BindingError");
    next.Should().ContainSingle(e => Attr(e, "Value") == "NetworkBindingErrorDlg");
    networkUi.Descendants(Wix + "Publish").Should().NotContain(e => Attr(e, "Property") == "SHORTCUT_HOST");
  }

  [Fact]
  public void NoActionPatchesTheShortcutFileAfterTheInstall() {
    var repoRoot = FindRepoRoot();
    var detection = File.ReadAllText(Path.Combine(repoRoot, "installer", "wix", "Fragments", "PortDetection.wxs"));
    var script = File.ReadAllText(Path.Combine(repoRoot, "installer", "wix", "Scripts", "PortResolver.jscript"));

    detection.Should().NotContain("NormalizeStartMenuShortcut");
    script.Should().NotContain("NormalizeStartMenuShortcut");
    script.Should().NotContain("updateShortcutHost");
  }

  [Fact]
  public void BothShortcutsStayInTheShortcutComponents() {
    var directories = LoadWix("Fragments", "Directories.wxs");
    var shortcuts = directories.Descendants(Wix + "Shortcut").ToList();

    var main = shortcuts.Single(e => Attr(e, "Id") == "GameBotStartMenuShortcut");
    Attr(main, "Arguments").Should().Be("url.dll,FileProtocolHandler [SHORTCUT_URL]");
    var background = shortcuts.Single(e => Attr(e, "Id") == "GameBotBackgroundManualStartShortcut");
    Attr(background, "Target").Should().Be("[APPLICATIONFOLDER]GameBot.Service.exe");
    Attr(background, "Arguments").Should().BeEmpty();
  }

  [Fact]
  public void RemovalFlowAvoidsConfigurationPagesAndDoesNotStartBackgroundApp() {
    var repoRoot = FindRepoRoot();
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");

    var product = File.ReadAllText(productPath);

    product.Should().Contain("Condition=\"WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 AND ((NOT Installed) OR (REINSTALL AND NOT REMOVE~=&quot;ALL&quot;))\"");

    var detection = LoadWix("Fragments", "PortDetection.wxs");
    foreach (var custom in detection.Descendants(Wix + "Custom").Where(e => Attr(e, "Action") == "ResolveNetworkBinding")) {
      Attr(custom, "Condition").Should().Contain("NOT REMOVE~=\"ALL\"");
    }
  }

  [Fact]
  public void MajorUpgradeRemovesTheOldProductInsideTheTransactionSoAFailedUpgradeRollsBack() {
    var product = LoadWix("Product.wxs");
    var upgrade = product.Descendants(Wix + "MajorUpgrade").Single();

    // With the default schedule (after InstallValidate) the old product is removed before the transaction starts.
    // A failure later cannot bring it back, so the shortcuts and registry entries are lost (FR-012, SC-005).
    // After InstallInitialize, the removal is part of the transaction and the rollback restores the old product.
    Attr(upgrade, "Schedule").Should().Be("afterInstallInitialize");
    Attr(upgrade, "AllowSameVersionUpgrades").Should().Be("yes");
  }

  [Fact]
  public void DowngradeFlowIsBlockedWithExplicitUserMessage() {
    var repoRoot = FindRepoRoot();
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");
    var bundlePath = Path.Combine(repoRoot, "installer", "wix", "Bundle.wxs");

    var product = File.ReadAllText(productPath);
    var bundle = File.ReadAllText(bundlePath);

    product.Should().Contain("<MajorUpgrade");
    product.Should().Contain("DowngradeErrorMessage=\"A newer version of GameBot is already installed.\"");
    bundle.Should().Contain("SuppressDowngradeFailure=\"no\"");
  }

  private static string Attr(XElement element, string name) => (string?)element.Attribute(name) ?? "";

  private static XDocument LoadWix(params string[] parts) =>
    XDocument.Load(Path.Combine(new[] { FindRepoRoot(), "installer", "wix" }.Concat(parts).ToArray()));

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
