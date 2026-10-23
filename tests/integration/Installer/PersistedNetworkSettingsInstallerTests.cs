using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

/// <summary>
/// Source tests of the WiX authoring for the saved host and port (feature 132).
/// They check the sequence, the conditions, and the action types. The cscript tests in
/// <see cref="PersistedNetworkScriptTests"/> check the behavior of the script.
/// </summary>
public sealed class PersistedNetworkSettingsInstallerTests {
  private static readonly XNamespace Wix = "http://wixtoolset.org/schemas/v4/wxs";

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

  private static string WixPath(params string[] parts) =>
    Path.Combine(new[] { RepoRoot(), "installer", "wix" }.Concat(parts).ToArray());

  private static XDocument Load(params string[] parts) => XDocument.Load(WixPath(parts));

  private static XDocument Properties() => Load("Fragments", "InstallerProperties.wxs");

  private static XElement SetProperty(XDocument document, string action) =>
    document.Descendants(Wix + "SetProperty").Single(e => (string?)e.Attribute("Action") == action);

  private static IEnumerable<XElement> SetPropertiesFor(XDocument document, string id) =>
    document.Descendants(Wix + "SetProperty").Where(e => (string?)e.Attribute("Id") == id);

  private static string Attr(XElement element, string name) => (string?)element.Attribute(name) ?? "";

  // ---- Resolver action (feature 162) ----

  [Fact]
  public void ResolverRunsAfterCostFinalizeInBothSequences() {
    var portDetection = Load("Fragments", "PortDetection.wxs");

    foreach (var sequenceName in new[] { "InstallUISequence", "InstallExecuteSequence" }) {
      var custom = portDetection.Descendants(Wix + sequenceName).Descendants(Wix + "Custom")
        .Single(e => Attr(e, "Action") == "ResolveNetworkBinding");
      Attr(custom, "After").Should().Be("CostFinalize", sequenceName);
      Attr(custom, "Condition").Should().Be(NotUninstall, sequenceName);
    }

    var action = portDetection.Descendants(Wix + "CustomAction").Single(e => Attr(e, "Id") == "ResolveNetworkBinding");
    Attr(action, "JScriptCall").Should().Be("ResolveNetworkBinding");
    Attr(action, "Execute").Should().Be("immediate");
    Attr(action, "Return").Should().Be("ignore");
  }

  [Fact]
  public void ResolverRunsOnceInTheExecuteSequence() {
    var portDetection = Load("Fragments", "PortDetection.wxs");

    portDetection.Descendants(Wix + "InstallExecuteSequence").Descendants(Wix + "Custom")
      .Count(e => Attr(e, "Action") == "ResolveNetworkBinding").Should().Be(1);
  }

  [Fact]
  public void InterfaceListIsFilledOnlyInAnInteractiveRun() {
    var portDetection = Load("Fragments", "PortDetection.wxs");

    var discover = portDetection.Descendants(Wix + "InstallUISequence").Descendants(Wix + "Custom")
      .Single(e => Attr(e, "Action") == "DiscoverBindInterfaces");
    Attr(discover, "After").Should().Be("ResolveNetworkBinding");
    Attr(discover, "Condition").Should().Contain("UILevel >= 5");
    portDetection.Descendants(Wix + "InstallExecuteSequence").Descendants(Wix + "Custom")
      .Should().NotContain(e => Attr(e, "Action") == "DiscoverBindInterfaces");
  }

  [Fact]
  public void ResolverReadsTheFileFromTheDataRootFolderProperty() {
    var script = File.ReadAllText(WixPath("Scripts", "PortResolver.jscript"));

    script.Should().Contain("Session.Property(\"DATAROOTFOLDER\")");
    script.Should().Contain("Session.Property(\"NETWORK_REGISTRY_ROOT\")");
  }

  [Fact]
  public void DataRootFolderIsUnderTheApplicationFolder() {
    var directories = Load("Fragments", "Directories.wxs");
    var dataRoot = directories.Descendants(Wix + "Directory").Single(e => Attr(e, "Id") == "DATAROOTFOLDER");

    Attr(dataRoot.Parent!, "Id").Should().Be("APPLICATIONFOLDER");
    Attr(dataRoot, "Name").Should().Be("data");
  }

  [Fact]
  public void NoSetPropertyChainRemainsForHostPortOrShortcut() {
    var rows = Properties().Descendants(Wix + "SetProperty").ToList();

    rows.Where(e => Attr(e, "Id") is "BIND_HOST" or "PORT" or "SHORTCUT_HOST" or "SHORTCUT_URL").Should().BeEmpty();
    rows.Where(e => Attr(e, "Id") == "PROTOCOL").Should().OnlyContain(e => !string.IsNullOrEmpty(Attr(e, "Action")));
  }

  [Fact]
  public void NoInstallerFileUsesTheRemovedPropertyNames() {
    foreach (var file in Directory.EnumerateFiles(WixPath(), "*.wxs", SearchOption.AllDirectories)
               .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))) {
      var text = File.ReadAllText(file);
      foreach (var name in new[] { "PERSISTED_BIND_HOST", "PERSISTED_PORT", "PERSISTED_FILE_BIND_HOST", "PERSISTED_FILE_PORT", "SHORTCUT_HOST" }) {
        text.Should().NotContain(name, file);
      }
    }

    File.ReadAllText(WixPath("Scripts", "PortResolver.jscript")).Should().NotContain("PERSISTED_");
  }

  [Fact]
  public void ProtocolRowsRunAfterTheRegistrySearch() {
    var properties = Properties();

    Attr(SetProperty(properties, "SetProtocolFromRegistry"), "After").Should().Be("AppSearch");
    Attr(SetProperty(properties, "SetProtocolDefault"), "After").Should().Be("SetProtocolFromRegistry");
  }

  [Fact]
  public void LegacyRegistryRootHasAProductionDefault() {
    var root = Properties().Descendants(Wix + "Property").Single(e => Attr(e, "Id") == "NETWORK_REGISTRY_ROOT");

    Attr(root, "Value").Should().Be(@"Software\GameBot\Network");
  }

  [Fact]
  public void DefaultsAreEmptyInTheBundleAndAreNotMsiProperties() {
    var bundle = Load("Bundle.wxs");
    var bindHost = bundle.Descendants(Wix + "Variable").Single(e => Attr(e, "Name") == "BIND_HOST");
    var port = bundle.Descendants(Wix + "Variable").Single(e => Attr(e, "Name") == "PORT");
    Attr(bindHost, "Value").Should().BeEmpty();
    Attr(port, "Value").Should().BeEmpty();

    var properties = Properties();
    properties.Descendants(Wix + "Property").Should().NotContain(e => Attr(e, "Id") == "BIND_HOST" && !string.IsNullOrEmpty(Attr(e, "Value")));
    properties.Descendants(Wix + "Property").Should().NotContain(e => Attr(e, "Id") == "PORT" && !string.IsNullOrEmpty(Attr(e, "Value")));
    properties.Descendants(Wix + "Property").Should().NotContain(e => Attr(e, "Id") == "SHORTCUT_URL");

    // The first-install defaults are in the resolver (FR-010).
    var script = File.ReadAllText(WixPath("Scripts", "PortResolver.jscript"));
    script.Should().Contain("var DEFAULT_BIND_HOST = \"127.0.0.1\";");
    script.Should().Contain("var DEFAULT_PORT_ORDER = [8080, 8088, 8888, 80];");
  }

  // ---- Write side (T022) ----

  private static XDocument ConfigTemplates() => Load("Fragments", "ConfigTemplates.wxs");

  private static XElement CustomActionDef(string id) =>
    ConfigTemplates().Descendants(Wix + "CustomAction").Single(e => Attr(e, "Id") == id);

  private static XElement ExecuteEntry(string action) =>
    ConfigTemplates().Descendants(Wix + "InstallExecuteSequence").Descendants(Wix + "Custom").Single(e => Attr(e, "Action") == action);

  private const string NotUninstall = "NOT REMOVE~=\"ALL\"";

  [Theory]
  [InlineData("WritePersistedNetworkFile", "SetWritePersistedNetworkFileData", "[DATAROOTFOLDER]|[BIND_HOST]|[PORT]")]
  [InlineData("RollbackPersistedNetworkFile", "SetRollbackPersistedNetworkFileData", "[DATAROOTFOLDER]")]
  [InlineData("CommitPersistedNetworkFile", "SetCommitPersistedNetworkFileData", "[DATAROOTFOLDER]|[NETWORK_REGISTRY_ROOT]")]
  public void ActionDataIsSetByAnImmediateRowAfterTheResolver(string property, string action, string value) {
    var row = SetProperty(ConfigTemplates(), action);

    Attr(row, "Id").Should().Be(property);
    Attr(row, "Value").Should().Be(value);
    Attr(row, "After").Should().Be("ResolveNetworkBinding");
    Attr(row, "Sequence").Should().Be("execute");
    Attr(row, "Condition").Should().Be(NotUninstall);
  }

  [Fact]
  public void WriteActionIsDeferredAfterInstallFiles() {
    var action = CustomActionDef("WritePersistedNetworkFile");
    Attr(action, "Execute").Should().Be("deferred");
    Attr(action, "Impersonate").Should().Be("yes");
    Attr(action, "JScriptCall").Should().Be("WritePersistedNetworkFile");
    Attr(action, "Return").Should().Be("ignore");

    var entry = ExecuteEntry("WritePersistedNetworkFile");
    Attr(entry, "After").Should().Be("InstallFiles");
    Attr(entry, "Condition").Should().Be(NotUninstall);
  }

  [Fact]
  public void RollbackActionRunsBeforeTheWriteAction() {
    var action = CustomActionDef("RollbackPersistedNetworkFile");
    Attr(action, "Execute").Should().Be("rollback");
    Attr(action, "Impersonate").Should().Be("yes");
    Attr(action, "JScriptCall").Should().Be("RollbackPersistedNetworkFile");
    Attr(action, "Return").Should().Be("ignore");

    var entry = ExecuteEntry("RollbackPersistedNetworkFile");
    Attr(entry, "Before").Should().Be("WritePersistedNetworkFile");
    Attr(entry, "Condition").Should().Be(NotUninstall);
  }

  [Fact]
  public void CommitActionRunsAfterTheWriteAction() {
    var action = CustomActionDef("CommitPersistedNetworkFile");
    Attr(action, "Execute").Should().Be("commit");
    Attr(action, "Impersonate").Should().Be("yes");
    Attr(action, "JScriptCall").Should().Be("CommitPersistedNetworkFile");
    Attr(action, "Return").Should().Be("ignore");

    var entry = ExecuteEntry("CommitPersistedNetworkFile");
    Attr(entry, "After").Should().Be("WritePersistedNetworkFile");
    Attr(entry, "Condition").Should().Be(NotUninstall);
  }

  [Fact]
  public void WriteActionDataComesAfterTheFinalValuesAreSet() {
    // The resolver sets the final values. The immediate row runs after it, so the host and port are final
    // when the row formats them.
    var row = SetProperty(ConfigTemplates(), "SetWritePersistedNetworkFileData");

    Attr(row, "After").Should().Be("ResolveNetworkBinding");
    Attr(row, "Value").Should().Contain("[BIND_HOST]").And.Contain("[PORT]");
  }

  // ---- One saved location (feature 162, US6) ----

  [Fact]
  public void TheRegistryComponentNoLongerWritesTheHostAndPort() {
    var component = ConfigTemplates().Descendants(Wix + "Component").Single(e => Attr(e, "Id") == "PersistNetworkConfigComponent");
    var names = component.Descendants(Wix + "RegistryValue").Select(e => Attr(e, "Name")).ToList();

    names.Should().Equal("Protocol");
  }

  [Fact]
  public void OnlyTheCommitActionTouchesTheOlderRegistryValues() {
    var script = File.ReadAllText(WixPath("Scripts", "PortResolver.jscript"));

    script.Should().Contain("RegDelete");
    // The rollback function and its core never delete a registry value.
    var rollbackStart = script.IndexOf("function RollbackNetworkFileCore", StringComparison.Ordinal);
    var rollbackEnd = script.IndexOf("function DeleteLegacyRegistryCore", StringComparison.Ordinal);
    script[rollbackStart..rollbackEnd].Should().NotContain("RegDelete");
  }

  // ---- The shortcut uses the final values (feature 162) ----

  [Fact]
  public void ShortcutUsesTheResolvedShortcutUrl() {
    var directories = File.ReadAllText(WixPath("Fragments", "Directories.wxs"));

    directories.Should().Contain("Arguments=\"url.dll,FileProtocolHandler [SHORTCUT_URL]\"");
    directories.Should().NotContain("localhost:[PORT]");
  }

  [Fact]
  public void TheWriteActionReceivesTheFinalValues() {
    var row = SetProperty(ConfigTemplates(), "SetWritePersistedNetworkFileData");

    // The row has no condition on the saved values, so it writes the explicit value, the saved value, or the default.
    Attr(row, "Condition").Should().Be(NotUninstall);
    Attr(row, "Value").Should().NotContain("PERSISTED_");
  }

  // ---- US3: uninstall keeps the file (T029) ----

  [Fact]
  public void NoMsiElementTracksTheNetworkFile() {
    foreach (var file in Directory.EnumerateFiles(WixPath(), "*.wxs", SearchOption.AllDirectories)) {
      var document = XDocument.Load(file);
      document.Descendants(Wix + "File").Should().NotContain(e => Attr(e, "Name").Contains("network.json", StringComparison.OrdinalIgnoreCase)
        || Attr(e, "Source").Contains("network.json", StringComparison.OrdinalIgnoreCase)
        || Attr(e, "Id").Contains("network", StringComparison.OrdinalIgnoreCase), file);
      document.Descendants(Wix + "RemoveFile").Should().NotContain(e => Attr(e, "Name").Contains("network", StringComparison.OrdinalIgnoreCase), file);
    }
  }

  [Fact]
  public void WriteStepDoesNotRunWhenUninstalling() {
    var configTemplates = ConfigTemplates();

    foreach (var action in new[] { "WritePersistedNetworkFile", "RollbackPersistedNetworkFile", "CommitPersistedNetworkFile" }) {
      Attr(ExecuteEntry(action), "Condition").Should().Contain(NotUninstall);
    }
    foreach (var action in new[] { "SetWritePersistedNetworkFileData", "SetRollbackPersistedNetworkFileData", "SetCommitPersistedNetworkFileData" }) {
      Attr(SetProperty(configTemplates, action), "Condition").Should().Contain(NotUninstall);
    }
  }

  [Fact]
  public void ProductReferencesTheFragmentsThatNoOtherElementReferences() {
    // A WiX fragment is in the MSI only when some element references it. Without these references
    // the property rows, the registry searches, and the resolver actions are not in the package.
    var product = Load("Product.wxs");
    var references = product.Descendants(Wix + "CustomActionRef").Select(e => Attr(e, "Id")).ToList();

    references.Should().Contain("SetApplicationFolder");
    references.Should().Contain("ResolveNetworkBinding");
    references.Should().Contain("ValidateNetworkBinding");
    references.Should().Contain("DiscoverBindInterfaces");
    references.Should().NotContain("DetectAvailablePorts");
    references.Should().NotContain("ReadPersistedNetworkFile");
    product.Descendants(Wix + "ComponentGroupRef").Select(e => Attr(e, "Id")).Should().Contain("PersistedConfigComponents");
  }

  [Fact]
  public void SetApplicationFolderOnlyRunsWhenNoFolderIsGiven() {
    var properties = Properties();
    var entries = properties.Descendants(Wix + "Custom").Where(e => Attr(e, "Action") == "SetApplicationFolder").ToList();

    entries.Should().HaveCount(2);
    entries.Should().OnlyContain(e => Attr(e, "Condition") == "APPLICATIONFOLDER = \"\"");
  }
}
