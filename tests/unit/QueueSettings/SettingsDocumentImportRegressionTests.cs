using System.Linq;
using FluentAssertions;
using GameBot.Domain.QueueSettings;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.QueueSettings;

/// <summary>
/// Feature 154 (plan, Loop 2 F1): the option change on SettingsDocumentJson.Options has no visible effect on the
/// queue settings import. The expected result is fixed in the test.
/// </summary>
public sealed class SettingsDocumentImportRegressionTests {
  private const string Document =
    "{\"formatVersion\":1,\"queues\":[],\"queueTemplates\":[{\"name\":\"Daily\",\"entries\":[]}],"
    + "\"sequences\":[{\"name\":\"S\",\"steps\":[]}],\"commands\":[],\"references\":[]}";

  [Fact]
  public void AValidDocumentGivesTheSameResult() {
    var result = SettingsDocumentValidator.Validate(Document);

    result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    result.Errors.Should().BeEmpty();
    result.Document!.FormatVersion.Should().Be(1);
    result.Document.QueueTemplates.Select(t => t.Name).Should().Equal("Daily");
    result.Document.Sequences.Should().HaveCount(1);
    result.Document.Commands.Should().BeEmpty();
    result.Document.Queues.Should().BeEmpty();
  }
}
