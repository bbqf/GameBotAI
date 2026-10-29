using System;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GameBot.IntegrationTests.Queues.FriendlyRunHelpers;

namespace GameBot.IntegrationTests.Queues;

/// <summary>Feature 168: a number and a pattern give the technical text in a text field of a command step.</summary>
[Collection("ConfigIsolation")]
public sealed class FriendlyNumberParameterRunTests {
  public FriendlyNumberParameterRunTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task NumberFiveAndPatternGiveEmulatorFive() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    var declaration = new ParameterDeclaration {
      Name = "instance", Type = ParameterValueType.Number, Pattern = "emulator-{value}"
    };
    await SeedAsync(
        services, KeyCommand("c-num1", "{{instance}}", declaration), SequenceInvoking("s-num1", "c-num1"), "q-num1",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "instance", Value = "5" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-num1").ConfigureAwait(false);

    var (text, _) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("instance=emulator-5");
  }

  [Fact]
  public async Task DefaultGetsThePatternToo() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    var declaration = new ParameterDeclaration {
      Name = "instance", Type = ParameterValueType.Number, Pattern = "emulator-{value}", Default = "7"
    };
    await SeedAsync(
        services, KeyCommand("c-num2", "{{instance}}", declaration), SequenceInvoking("s-num2", "c-num2"), "q-num2")
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-num2").ConfigureAwait(false);

    var (text, _) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("instance=emulator-7");
  }
}
