using System.Text.Json;
using FluentAssertions;
using GameBot.Service.StartupValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.StartupValidation;

public sealed class LegacyActionReferenceScannerTests : IDisposable {
  private readonly string _storageRoot;

  public LegacyActionReferenceScannerTests() {
    _storageRoot = Path.Combine(Path.GetTempPath(), "gamebot-cutover-scan-", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_storageRoot);
  }

  [Fact]
  public async Task ScanAsyncReturnsCleanReportWhenNoLegacyReferencesExist() {
    var scanner = new LegacyActionReferenceScanner(_storageRoot, NullLogger<LegacyActionReferenceScanner>.Instance, () => new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    var report = await scanner.ScanAsync();

    report.IsBlocked.Should().BeFalse();
    report.Issues.Should().BeEmpty();
    report.CheckedAtUtc.Should().Be(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
  }

  [Fact]
  public async Task ScanAsyncReportsLegacyActionFilesAndActionStepReferences() {
    var actionsRoot = Path.Combine(_storageRoot, "actions");
    Directory.CreateDirectory(actionsRoot);
    await File.WriteAllTextAsync(Path.Combine(actionsRoot, "legacy-action.json"), JsonSerializer.Serialize(new {
      id = "action-1",
      name = "Legacy Action"
    }));

    var commandsRoot = Path.Combine(_storageRoot, "commands");
    Directory.CreateDirectory(commandsRoot);
    await File.WriteAllTextAsync(Path.Combine(commandsRoot, "legacy-command.json"), """
      {
        "id": "command-1",
        "name": "Legacy Command",
        "steps": [
          { "type": "Action", "targetId": "action-1", "order": 0 },
          { "type": 1, "targetId": "action-2", "order": 1 }
        ]
      }
      """);

    var scanner = new LegacyActionReferenceScanner(_storageRoot, NullLogger<LegacyActionReferenceScanner>.Instance, () => new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    var report = await scanner.ScanAsync();

    report.IsBlocked.Should().BeTrue();
    report.Issues.Should().ContainSingle(issue => issue.Store == "actions" && issue.Path == Path.Combine("actions", "legacy-action.json") && issue.ReferenceCount == 1);
    report.Issues.Should().ContainSingle(issue => issue.Store == "commands" && issue.Path == Path.Combine("commands", "legacy-command.json") && issue.ReferenceCount == 2);
  }

  public void Dispose() {
    if (Directory.Exists(_storageRoot)) {
      Directory.Delete(_storageRoot, recursive: true);
    }
  }
}