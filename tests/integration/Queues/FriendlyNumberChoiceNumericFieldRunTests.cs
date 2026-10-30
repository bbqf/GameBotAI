#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GameBot.IntegrationTests.Queues.FriendlyRunHelpers;

namespace GameBot.IntegrationTests.Queues;

/// <summary>Feature 168: a number choice fills a numeric field of a command step, and the log has the label.</summary>
[Collection("ConfigIsolation")]
public sealed class FriendlyNumberChoiceNumericFieldRunTests {
  public FriendlyNumberChoiceNumericFieldRunTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task NumberChoiceFillsTheNumericFieldAndTheLogHasTheLabel() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    var difficulty = new ParameterDeclaration {
      Name = "swipeMs", Type = ParameterValueType.Number, Kind = ParameterInputKind.Choice,
      Options = new[] {
        new ParameterOption { Label = "Easy", Value = "100" },
        new ParameterOption { Label = "Hard mode", Value = "300" }
      }
    };
    var command = new Command { Id = "c-numchoice", Name = "Swipe" };
    command.Parameters.Add(difficulty);
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 0,
      Swipe = new SwipeConfig { StartX = 1, StartY = 2, EndX = 3, EndY = 4 },
      FieldTemplates = new Dictionary<string, string> { ["swipe.durationMs"] = "{{swipeMs}}" }
    });

    await SeedAsync(
        services, command, SequenceInvoking("s-numchoice", "c-numchoice"), "q-numchoice",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "swipeMs", Value = "300" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-numchoice").ConfigureAwait(false);

    var (text, data) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("swipeMs=300 (Hard mode)");
    data.Should().Contain("\"label\":\"Hard mode\"");
  }
}
