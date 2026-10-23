using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

/// <summary>
/// Checks the isolated test variant of the MSI (feature 162, research R12). A test install of the real MSI would
/// replace the production product, because the MSI is per-user and has fixed identities. The script
/// <c>scripts/installer/new-test-variant.ps1</c> makes a copy that shares nothing with production.
/// The tests that open an MSI need a built MSI under <c>installer/wix/bin</c>. They return early without one.
/// </summary>
public sealed class TestVariantTests : IDisposable {
  private const string ProductionUpgradeCode = "{B7CA67D4-9DE5-43F5-BE43-EA9A00A3E202}";

  private readonly string _root = Path.Combine(Path.GetTempPath(), "gb-variant-" + Guid.NewGuid().ToString("N"));

  public TestVariantTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    GC.SuppressFinalize(this);
  }

  private static string RepoRoot() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null) {
      if (File.Exists(Path.Combine(dir.FullName, "GameBot.sln"))) {
        return dir.FullName;
      }

      dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root containing GameBot.sln");
  }

  private static string? BuiltMsi() {
    // GAMEBOT_TEST_MSI points to an MSI that was built from this branch (for example from a scratch build).
    var fromEnvironment = Environment.GetEnvironmentVariable("GAMEBOT_TEST_MSI");
    if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment)) {
      return fromEnvironment;
    }

    foreach (var configuration in new[] { "Release", "Debug" }) {
      var path = Path.Combine(RepoRoot(), "installer", "wix", "bin", "x64", configuration, "GameBot.msi");
      if (File.Exists(path)) {
        return path;
      }
    }

    return null;
  }

  // An MSI that was built before this feature has no NETWORK_REGISTRY_ROOT property. The script refuses it,
  // so these tests return early until the MSI is built again from this branch.
  private static bool CanRun => OperatingSystem.IsWindows() && BuiltMsi() is { } msi && HasNetworkRegistryRoot(msi);

  private static bool HasNetworkRegistryRoot(string msi) =>
    Rows(msi, "SELECT `Value` FROM `Property` WHERE `Property`='NETWORK_REGISTRY_ROOT'", 1).Count > 0;

  private string MakeVariant(string instance) {
    var output = Path.Combine(_root, "GameBot." + instance + ".msi");
    var script = Path.Combine(RepoRoot(), "scripts", "installer", "new-test-variant.ps1");
    var info = new ProcessStartInfo("powershell.exe",
      $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -MsiPath \"{BuiltMsi()}\" -InstanceName {instance} -OutputPath \"{output}\"") {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    using var process = Process.Start(info)!;
    var outputText = process.StandardOutput.ReadToEnd();
    var errorText = process.StandardError.ReadToEnd();
    process.WaitForExit();
    process.ExitCode.Should().Be(0, "the script must run. Output: " + outputText + errorText);
    return output;
  }

  private static List<string[]> Rows(string msi, string sql, int columns) {
    var type = Type.GetTypeFromProgID("WindowsInstaller.Installer")!;
    dynamic installer = Activator.CreateInstance(type)!;
    dynamic database = installer.OpenDatabase(msi, 0);
    dynamic view = database.OpenView(sql);
    view.Execute();
    var rows = new List<string[]>();
    while (true) {
      dynamic? record = view.Fetch();
      if (record is null) {
        break;
      }

      var values = new string[columns];
      for (var i = 1; i <= columns; i++) {
        values[i - 1] = (string?)record.StringData[i] ?? string.Empty;
      }

      rows.Add(values);
    }

    view.Close();
    return rows;
  }

  private static string Property(string msi, string name) =>
    Rows(msi, "SELECT `Value` FROM `Property` WHERE `Property`='" + name + "'", 1).Single()[0];

  // ---- Source tests: they always run ----

  [Fact]
  public void TheDefaultPackageKeepsTheProductionUpgradeCodeAndRegistryRoot() {
    var product = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "wix", "Product.wxs"));
    var properties = File.ReadAllText(Path.Combine(RepoRoot(), "installer", "wix", "Fragments", "InstallerProperties.wxs"));

    product.Should().Contain("UpgradeCode=\"B7CA67D4-9DE5-43F5-BE43-EA9A00A3E202\"");
    properties.Should().Contain("Property Id=\"NETWORK_REGISTRY_ROOT\" Value=\"Software\\GameBot\\Network\"");
  }

  [Fact]
  public void TheScriptChangesEveryProductionIdentity() {
    var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "installer", "new-test-variant.ps1"));

    foreach (var expected in new[] { "UpgradeCode", "ProductCode", "ComponentId", "NETWORK_REGISTRY_ROOT", "ApplicationProgramsFolder", "CurrentVersion\\Run", "SetApplicationFolder" }) {
      script.Should().Contain(expected);
    }
  }

  [Fact]
  public void TheScriptNeverTouchesTheSourceMsiOrTheRegistry() {
    var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "installer", "new-test-variant.ps1"));

    script.Should().Contain("Copy-Item -LiteralPath $MsiPath -Destination $OutputPath");
    script.Should().NotContain("Set-ItemProperty -Path HKCU");
    script.Should().NotContain("New-ItemProperty");
    script.Should().NotContain("Remove-Item HKCU");
  }

  // ---- Tests on a real MSI: they return early when no MSI is built ----

  [Fact]
  public void TheVariantAndTheDefaultMsiShareNoComponentGuid() {
    if (!CanRun) { return; }
    var variant = MakeVariant("TA");

    var defaultGuids = Rows(BuiltMsi()!, "SELECT `ComponentId` FROM `Component`", 1).Select(r => r[0]).ToList();
    var variantGuids = Rows(variant, "SELECT `ComponentId` FROM `Component`", 1).Select(r => r[0]).ToList();

    variantGuids.Should().HaveCount(defaultGuids.Count);
    variantGuids.Intersect(defaultGuids, StringComparer.OrdinalIgnoreCase).Should().BeEmpty();
    variantGuids.Should().OnlyHaveUniqueItems();
  }

  [Fact]
  public void TheVariantHasItsOwnUpgradeCodeProductCodeAndNetworkRoot() {
    if (!CanRun) { return; }
    var variant = MakeVariant("TB");

    Property(variant, "UpgradeCode").Should().NotBe(ProductionUpgradeCode);
    Property(variant, "ProductCode").Should().NotBe(Property(BuiltMsi()!, "ProductCode"));
    Property(variant, "NETWORK_REGISTRY_ROOT").Should().Be(@"Software\GameBotTest\TB\Network");
    Property(BuiltMsi()!, "NETWORK_REGISTRY_ROOT").Should().Be(@"Software\GameBot\Network");
    Rows(variant, "SELECT `UpgradeCode` FROM `Upgrade`", 1).Should().OnlyContain(r => r[0] == Property(variant, "UpgradeCode"));
  }

  [Fact]
  public void TheVariantUsesNoProductionRegistryKeyFolderOrAutostartName() {
    if (!CanRun) { return; }
    var variant = MakeVariant("TC");

    var keys = Rows(variant, "SELECT `Key`, `Name` FROM `Registry`", 2);
    keys.Should().NotContain(r => r[0] == @"Software\GameBot" || r[0].StartsWith(@"Software\GameBot\", StringComparison.Ordinal));
    keys.Should().NotContain(r => r[0].EndsWith(@"CurrentVersion\Run", StringComparison.Ordinal) && r[1] == "GameBot");
    Rows(variant, "SELECT `Key` FROM `RegLocator`", 1).Should().NotContain(r => r[0].StartsWith(@"Software\GameBot\", StringComparison.Ordinal));
    var directories = Rows(variant, "SELECT `Directory`, `DefaultDir` FROM `Directory`", 2);
    directories.Single(r => r[0] == "ApplicationProgramsFolder")[1].Should().Be("GameBotTestTC");
    directories.Single(r => r[0] == "APPLICATIONFOLDER")[1].Should().Be("GameBotTestTC");
  }

  [Fact]
  public void TwoVariantsOfDifferentInstancesShareNothingWithEachOther() {
    if (!CanRun) { return; }
    var first = MakeVariant("TD");
    var second = MakeVariant("TE");

    var a = Rows(first, "SELECT `ComponentId` FROM `Component`", 1).Select(r => r[0]);
    var b = Rows(second, "SELECT `ComponentId` FROM `Component`", 1).Select(r => r[0]);
    a.Intersect(b, StringComparer.OrdinalIgnoreCase).Should().BeEmpty();
    Property(first, "UpgradeCode").Should().NotBe(Property(second, "UpgradeCode"));
  }

  [Fact]
  public void TheDefaultMsiIsNotChangedByMakingAVariant() {
    if (!CanRun) { return; }
    var before = File.GetLastWriteTimeUtc(BuiltMsi()!);
    var length = new FileInfo(BuiltMsi()!).Length;

    MakeVariant("TF");

    File.GetLastWriteTimeUtc(BuiltMsi()!).Should().Be(before);
    new FileInfo(BuiltMsi()!).Length.Should().Be(length);
    Property(BuiltMsi()!, "UpgradeCode").Should().Be(ProductionUpgradeCode);
    CultureInfo.CurrentCulture.Should().NotBeNull();
  }
}
