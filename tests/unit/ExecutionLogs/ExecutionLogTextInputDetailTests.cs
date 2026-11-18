using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

/// <summary>Feature 192 (FR-023): the log detail item of a TextInput step holds the length, not the text.</summary>
public sealed class ExecutionLogTextInputDetailTests {
  [Fact]
  public async Task AnExecutedTextInputStepGivesOneDetailItemWithTheTypedLength() {
    var storageRoot = Path.Combine(Path.GetTempPath(), "GameBot.UnitTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(storageRoot);
    using var repository = new FileExecutionLogRepository(storageRoot);
    using var retention = new ExecutionLogRetentionPolicyRepository(storageRoot);
    var service = new ExecutionLogService(repository, retention);
    var outcome = new PrimitiveTapStepOutcome(1, "executed", null, null, null, StepType: "text-input", TypedLength: 3);

    await service.LogCommandExecutionAsync("cmd1", "Type amount", "success", new[] { outcome }, null, 0).ConfigureAwait(false);

    var page = await repository.QueryAsync(new ExecutionLogQuery()).ConfigureAwait(false);
    var entry = page.Items.Should().ContainSingle().Subject;
    var item = entry.Details.Should().ContainSingle(d => d.Kind == "text-input").Subject;
    item.Message.Should().Be("Text input typed 3 characters.");
    item.Attributes.Should().ContainKey("typedLength");
    item.Attributes!["typedLength"]!.ToString().Should().Be("3");
    item.Attributes.Keys.Should().BeEquivalentTo(new[] { "typedLength" });
    entry.Details.Select(d => d.Message).Should().NotContain(m => m.Contains("250", StringComparison.Ordinal));
  }
}
