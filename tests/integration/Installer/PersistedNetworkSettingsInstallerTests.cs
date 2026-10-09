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

  // ---- Read side (T017) ----

  [Fact]
  public void ReadActionRunsAfterCostFinalizeInBothSequences() {
    var portDetection = Load("Fragments", "PortDetection.wxs");

    foreach (var sequenceName in new[] { "InstallUISequence", "InstallExecuteSequence" }) {
      var custom = portDetection.Descendants(Wix + sequenceName).Descendants(Wix + "Custom")
        .Single(e => Attr(e, "Action") == "ReadPersistedNetworkFile");
      Attr(custom, "After").Should().Be("CostFinalize", sequenceName);
    }

    var action = portDetection.Descendants(Wix + "CustomAction").Single(e => Attr(e, "Id") == "ReadPersistedNetworkFile");
    Attr(action, "JScriptCall").Should().Be("ReadPersistedNetworkFile");
    Attr(action, "Execute").Should().Be("immediate");
    Attr(action, "Return").Should().Be("ignore");
  }

  [Fact]
  public void ReadActionUsesTheDataRootFolderProperty() {
    var script = File.ReadAllText(WixPath("Scripts", "PortResolver.jscript"));

    script.Should().Contain("Session.Property(\"DATAROOTFOLDER\")");
    script.Should().Contain("PERSISTED_FILE_BIND_HOST");
    script.Should().Contain("PERSISTED_FILE_PORT");
  }

  [Fact]
  public void DataRootFolderIsUnderTheApplicationFolder() {
    var directories = Load("Fragments", "Directories.wxs");
    var dataRoot = directories.Descendants(Wix + "Directory").Single(e => Attr(e, "Id") == "DATAROOTFOLDER");

    Attr(dataRoot.Parent!, "Id").Should().Be("APPLICATIONFOLDER");
    Attr(dataRoot, "Name").Should().Be("data");
  }

  [Fact]
  public void EveryPropertyRowHasAnExplicitActionName() {
    var properties = Properties();
    var rows = properties.Descendants(Wix + "SetProperty")
      .Where(e => Attr(e, "Id") is "BIND_HOST" or "PORT" or "PROTOCOL" or "SHORTCUT_HOST" or "SHORTCUT_URL")
      .ToList();

    rows.Should().NotBeEmpty();
    rows.Should().OnlyContain(e => !string.IsNullOrEmpty(Attr(e, "Action")));
    rows.Select(e => Attr(e, "Action")).Should().OnlyHaveUniqueItems();
  }

  [Fact]
  public void PropertyRowsRunAfterTheReadActionInAFixedChain() {
    var properties = Properties();
    var portDetection = Load("Fragments", "PortDetection.wxs");

    // Every row (and the port detection) is placed After another action, never Before CostFinalize.
    var rows = properties.Descendants(Wix + "SetProperty")
      .Where(e => Attr(e, "Id") is "BIND_HOST" or "PORT" or "PROTOCOL" or "SHORTCUT_HOST" or "SHORTCUT_URL")
      .ToList();
    rows.Should().OnlyContain(e => string.IsNullOrEmpty(Attr(e, "Before")) && !string.IsNullOrEmpty(Attr(e, "After")));

    // Follow the chain from each row to the read action. A loop or a break fails the test.
    var after = rows.ToDictionary(e => Attr(e, "Action"), e => Attr(e, "After"));
    foreach (var detect in portDetection.Descendants(Wix + "Custom")) {
      var name = Attr(detect, "Action");
      if (name is "DiscoverBindInterfaces" or "DetectAvailablePorts" && !string.IsNullOrEmpty(Attr(detect, "After"))) {
        after[name] = Attr(detect, "After");
      }
    }
    after["DiscoverBindInterfaces"] = "ReadPersistedNetworkFile";
    after["DetectAvailablePorts"] = "DiscoverBindInterfaces";

    foreach (var start in rows.Select(e => Attr(e, "Action"))) {
      var current = start;
      var hops = 0;
      while (current != "ReadPersistedNetworkFile") {
        after.Should().ContainKey(current, "the chain of " + start + " must reach the read action");
        current = after[current];
        (++hops).Should().BeLessThan(60, "the chain of " + start + " must not loop");
      }
    }
  }

  [Fact]
  public void PortDetectionRunsAfterTheReadActionAndSkipsWhenAFileValueExists() {
    var portDetection = Load("Fragments", "PortDetection.wxs");

    foreach (var custom in portDetection.Descendants(Wix + "Custom")
               .Where(e => Attr(e, "Action") is "DiscoverBindInterfaces" or "DetectAvailablePorts")) {
      Attr(custom, "Before").Should().BeEmpty();
      Attr(custom, "After").Should().NotBeEmpty();
      Attr(custom, "Condition").Should().Contain("PERSISTED_FILE_PORT = \"\"");
      Attr(custom, "Condition").Should().Contain("PERSISTED_FILE_BIND_HOST = \"\"");
    }

    var uiDetect = portDetection.Descendants(Wix + "InstallUISequence").Descendants(Wix + "Custom")
      .Single(e => Attr(e, "Action") == "DetectAvailablePorts");
    var executeDetect = portDetection.Descendants(Wix + "InstallExecuteSequence").Descendants(Wix + "Custom")
      .Single(e => Attr(e, "Action") == "DetectAvailablePorts");
    Attr(uiDetect, "After").Should().Be("DiscoverBindInterfaces");
    Attr(executeDetect, "After").Should().Be("ReadPersistedNetworkFile");
  }

  [Fact]
  public void FirstDialogSkipConditionsAlsoTestTheFileValues() {
    var product = File.ReadAllText(WixPath("Product.wxs"));

    product.Should().Contain("PERSISTED_FILE_PORT = &quot;&quot; AND PERSISTED_FILE_BIND_HOST = &quot;&quot;");
    product.Should().Contain("PERSISTED_FILE_PORT &lt;&gt; &quot;&quot; OR PERSISTED_FILE_BIND_HOST &lt;&gt; &quot;&quot;");
  }

  [Theory]
  [InlineData("BIND_HOST", "PERSISTED_FILE_BIND_HOST", "PERSISTED_BIND_HOST")]
  [InlineData("PORT", "PERSISTED_FILE_PORT", "PERSISTED_PORT")]
  public void EachFieldAppliesExplicitThenFileThenRegistryOnlyWhileEmpty(string property, string fileProperty, string registryProperty) {
    var rows = SetPropertiesFor(Properties(), property).ToList();
    var fromFile = rows.Single(e => Attr(e, "Value") == "[" + fileProperty + "]");
    var fromRegistry = rows.Single(e => Attr(e, "Value") == "[" + registryProperty + "]");

    Attr(fromFile, "Condition").Should().Contain(property + " = \"\"");
    Attr(fromFile, "Condition").Should().Contain(fileProperty + " <> \"\"");
    Attr(fromRegistry, "Condition").Should().Contain(property + " = \"\"");
    Attr(fromRegistry, "Condition").Should().Contain(registryProperty + " <> \"\"");

    // File first, then registry, then default (a row that runs later has the file row before it).
    Attr(fromRegistry, "After").Should().Be(Attr(fromFile, "Action"));
  }

  [Fact]
  public void HostAndPortUseSeparateRows() {
    var properties = Properties();

    SetPropertiesFor(properties, "BIND_HOST").Should().NotBeEmpty();
    SetPropertiesFor(properties, "PORT").Should().NotBeEmpty();
    SetPropertiesFor(properties, "BIND_HOST").Select(e => Attr(e, "Condition")).Should().NotContain(c => c.Contains("PERSISTED_FILE_PORT", StringComparison.Ordinal));
    SetPropertiesFor(properties, "PORT").Select(e => Attr(e, "Condition")).Should().NotContain(c => c.Contains("PERSISTED_FILE_BIND_HOST", StringComparison.Ordinal));
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

    // The last-step rows keep the first-install defaults (FR-010).
    SetPropertiesFor(properties, "BIND_HOST").Should().Contain(e => Attr(e, "Value") == "127.0.0.1");
    SetPropertiesFor(properties, "PORT").Should().Contain(e => Attr(e, "Value") == "8080");
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
  [InlineData("CommitPersistedNetworkFile", "SetCommitPersistedNetworkFileData", "[DATAROOTFOLDER]")]
  public void ActionDataIsSetByAnImmediateRowBeforeInstallInitialize(string property, string action, string value) {
    var row = SetProperty(ConfigTemplates(), action);

    Attr(row, "Id").Should().Be(property);
    Attr(row, "Value").Should().Be(value);
    Attr(row, "Before").Should().Be("InstallInitialize");
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
    // The immediate row runs Before InstallInitialize. The chain of the final values runs after CostFinalize,
    // so the host and port are final when the row formats them.
    var row = SetProperty(ConfigTemplates(), "SetWritePersistedNetworkFileData");

    Attr(row, "Before").Should().Be("InstallInitialize");
    Attr(row, "Value").Should().Contain("[BIND_HOST]").And.Contain("[PORT]");
  }

  // ---- US1: the shortcut uses the final values (T026) ----

  [Fact]
  public void ShortcutUsesTheFinalPortAndTheHostRule() {
    var directories = File.ReadAllText(WixPath("Fragments", "Directories.wxs"));
    directories.Should().Contain("FileProtocolHandler http://localhost:[PORT]/");

    var properties = Properties();
    var loopback = SetProperty(properties, "SetShortcutHostLoopback");
    Attr(loopback, "Value").Should().Be("localhost");
    Attr(loopback, "Condition").Should().Contain("127.0.0.1").And.Contain("0.0.0.0");
    var custom = SetProperty(properties, "SetShortcutHostCustom");
    Attr(custom, "Value").Should().Be("[BIND_HOST]");

    // The shortcut rows run after the PORT and BIND_HOST rows (the chain order is in the property file).
    var order = properties.Descendants(Wix + "SetProperty").Select(e => Attr(e, "Action")).ToList();
    order.IndexOf("SetShortcutHostLoopback").Should().BeGreaterThan(order.IndexOf("SetPortDefault"));
    order.IndexOf("SetShortcutUrlLoopback").Should().BeGreaterThan(order.IndexOf("SetBindHostDefault"));
    Attr(SetProperty(properties, "SetShortcutHostLoopback"), "After").Should().Be("SetProtocolDefault");
    Attr(SetProperty(properties, "SetProtocolFromRegistry"), "After").Should().Be("SetPortDefault");
  }

  // ---- US2: an explicit option wins (T028) ----

  [Fact]
  public void ExplicitOptionIsNeverReplacedBySavedValue() {
    var properties = Properties();
    var savedValueRows = properties.Descendants(Wix + "SetProperty")
      .Where(e => Attr(e, "Id") is "BIND_HOST" or "PORT" && Attr(e, "Value").StartsWith("[PERSISTED_", StringComparison.Ordinal));

    savedValueRows.Should().HaveCount(4);
    savedValueRows.Should().OnlyContain(e => Attr(e, "Condition").StartsWith(Attr(e, "Id") + " = \"\" AND ", StringComparison.Ordinal));
  }

  [Fact]
  public void RegistryValueWithNoFileStillGivesTheSavedValue() {
    var properties = Properties();
    var fromRegistry = SetProperty(properties, "SetPortFromRegistry");

    Attr(fromRegistry, "Condition").Should().NotContain("PERSISTED_FILE_PORT");
    Attr(fromRegistry, "After").Should().Be("SetPortFromFile");
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
  public void FirstInstallDefaultsAndPortDetectionStillApplyWithNoSavedValue() {
    var properties = Properties();
    SetProperty(properties, "SetBindHostDefault").Attribute("Value")!.Value.Should().Be("127.0.0.1");
    SetProperty(properties, "SetPortDefault").Attribute("Value")!.Value.Should().Be("8080");

    var portDetection = File.ReadAllText(WixPath("Fragments", "PortDetection.wxs"));
    portDetection.Should().Contain("NOT Installed AND NOT WIX_UPGRADE_DETECTED AND PERSISTED_PORT = &quot;&quot;");
    portDetection.Should().Contain("Action=\"DetectAvailablePorts\"");
  }

  [Fact]
  public void ProductReferencesTheFragmentsThatNoOtherElementReferences() {
    // A WiX fragment is in the MSI only when some element references it. Without these references
    // the property rows, the registry searches, and the read action are not in the package.
    var product = Load("Product.wxs");
    var references = product.Descendants(Wix + "CustomActionRef").Select(e => Attr(e, "Id")).ToList();

    references.Should().Contain("SetApplicationFolder");
    references.Should().Contain("ReadPersistedNetworkFile");
    references.Should().Contain("DetectAvailablePorts");
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
