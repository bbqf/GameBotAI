using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionNavigationIntegrationTests
{
  [Fact]
  public async Task StandaloneSequenceHasDirectPathAndNoParentPath()
  {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();

    var logService = app.Services.GetRequiredService<IExecutionLogService>();

    await logService.LogSequenceExecutionAsync(
      "seq-alone",
      "Standalone Sequence",
      "success",
      "Standalone execution",
      new ExecutionLogContext { Depth = 0 },
      details: null).ConfigureAwait(false);

    var sequence = (await logService.QueryAsync(new ExecutionLogQuery
    {
      ObjectType = "sequence",
      ObjectId = "seq-alone",
      PageSize = 1
    }).ConfigureAwait(false)).Items.Single();

    sequence.Navigation.DirectPath.Should().Be("/authoring/sequences/seq-alone");
    sequence.Navigation.ParentPath.Should().BeNull();
  }
}
