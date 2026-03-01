using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionLogConcisenessIntegrationTests
{
  [Fact]
  public async Task SequenceLogTrimsSummaryAndTruncatesDetailsWithMarker()
  {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();

    var logService = app.Services.GetRequiredService<IExecutionLogService>();

    var longSummary = new string('x', 500);
    var details = Enumerable.Range(1, 15)
      .Select(i => new ExecutionDetailItem("detail", $"detail-{i}", null, "normal"))
      .ToArray();

    await logService.LogSequenceExecutionAsync(
      "seq-concise",
      "Concise Sequence",
      "success",
      longSummary,
      new ExecutionLogContext { Depth = 0 },
      details).ConfigureAwait(false);

    var item = (await logService.QueryAsync(new ExecutionLogQuery
    {
      ObjectType = "sequence",
      ObjectId = "seq-concise",
      PageSize = 1
    }).ConfigureAwait(false)).Items.Single();

    item.Summary.Length.Should().BeLessOrEqualTo(240);
    item.Details.Should().HaveCount(10);
    item.Details[9].Kind.Should().Be("meta");
    item.Details[9].Message.Should().Be("Additional details were truncated.");
  }
}
