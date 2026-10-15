using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GameBot.Domain.QueueSettings;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Xunit;

#pragma warning disable CA2007, CA1707

namespace GameBot.UnitTests.QueueSettings;

public sealed class SettingsNormalizerTests {
  [Fact]
  public async Task SequenceFormHasNoIdAndNoTimeStamp() {
    using var kit = new QueueSettingsTestKit();
    var command = await kit.AddCommandAsync("Back");
    var sequence = await kit.AddSequenceAsync("Daily", command.Id);

    var doc = SettingsNormalizer.SequenceToDoc(sequence, id => id == command.Id ? command.Name : null);

    var text = doc.ToJsonString();
    Assert.DoesNotContain(sequence.Id, text, StringComparison.Ordinal);
    Assert.DoesNotContain(command.Id, text, StringComparison.Ordinal);
    Assert.DoesNotContain("createdAt", text, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("updatedAt", text, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("\"id\"", text, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("\"Back\"", text, StringComparison.Ordinal);
  }

  [Fact]
  public async Task CommandFormHasNoIdAndNoTriggerId() {
    using var kit = new QueueSettingsTestKit();
    var inner = await kit.AddCommandAsync("Inner");
    var outer = await kit.AddCommandAsync("Outer", inner.Id);

    var doc = SettingsNormalizer.CommandToDoc(outer, id => id == inner.Id ? inner.Name : null);

    var text = doc.ToJsonString();
    Assert.DoesNotContain(outer.Id, text, StringComparison.Ordinal);
    Assert.DoesNotContain(inner.Id, text, StringComparison.Ordinal);
    Assert.DoesNotContain("triggerId", text, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("Inner", text, StringComparison.Ordinal);
  }

  [Fact]
  public async Task SameInputGivesSameOutputWithSortedKeys() {
    using var kit = new QueueSettingsTestKit();
    var command = await kit.AddCommandAsync("Back");
    var sequence = await kit.AddSequenceAsync("Daily", command.Id);

    var first = SettingsNormalizer.SequenceToDoc(sequence, id => command.Name).ToJsonString();
    var second = SettingsNormalizer.SequenceToDoc(sequence, id => command.Name).ToJsonString();

    Assert.Equal(first, second);
    var keys = JsonNode.Parse(first)!.AsObject().Select(p => p.Key).ToList();
    Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys);
  }

  [Fact]
  public void CanonicalTextIgnoresMemberOrderAndCase() {
    var a = QueueSettingsTestKit.Obj(QueueSettingsTestKit.SequenceJson("Daily", "Back"));
    var b = QueueSettingsTestKit.Obj("{\"steps\":[{\"stepType\":\"Command\",\"commandId\":\"Back\",\"stepId\":\"s0\",\"order\":0}],\"version\":1,\"name\":\"Daily\"}");

    Assert.Equal(SettingsNormalizer.CanonicalSequence(a), SettingsNormalizer.CanonicalSequence(b));
  }
}
