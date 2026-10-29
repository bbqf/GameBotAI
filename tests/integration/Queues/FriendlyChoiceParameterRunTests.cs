using System;
using System.Collections.ObjectModel;
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

/// <summary>Feature 168: a choice parameter reaches the step as the technical value, and the log has the label.</summary>
[Collection("ConfigIsolation")]
public sealed class FriendlyChoiceParameterRunTests {
  public FriendlyChoiceParameterRunTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static ParameterDeclaration Mode(string name = "mode") => new() {
    Name = name, Kind = ParameterInputKind.Choice,
    Options = new[] {
      new ParameterOption { Label = "Go home", Value = "KEYCODE_HOME" },
      new ParameterOption { Label = "Go back", Value = "KEYCODE_BACK" }
    }
  };

  [Fact]
  public async Task LogShowsTheTechnicalValueAndTheOptionLabel() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(
        services, KeyCommand("c-ch1", "{{mode}}", Mode()), SequenceInvoking("s-ch1", "c-ch1"), "q-ch1",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "mode", Value = "KEYCODE_BACK" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-ch1").ConfigureAwait(false);

    var (text, data) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("mode=KEYCODE_BACK");
    data.Should().Contain("\"label\":\"Go back\"");
  }

  [Fact]
  public async Task MaskedNameHasNoLabel() {
    // FR-017: a name that matches the secret-name rule stays masked, and its label is not shown.
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(
        services, KeyCommand("c-ch2", "{{password}}", Mode("password")), SequenceInvoking("s-ch2", "c-ch2"), "q-ch2",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "password", Value = "KEYCODE_BACK" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-ch2").ConfigureAwait(false);

    var (_, data) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    data.Should().NotContain("Go back");
  }

  [Fact]
  public async Task SequenceParameterPassesThroughToTheCommandChoice() {
    // FR-028: the step binding {{seqMode}} gives the sequence value to the command parameter.
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    var bindings = new Collection<ParameterBinding> { new() { Name = "mode", Value = "{{seqMode}}" } };
    await SeedAsync(
        services, KeyCommand("c-ch3", "{{mode}}", Mode()),
        SequenceInvoking("s-ch3", "c-ch3", bindings, Mode("seqMode")), "q-ch3",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "seqMode", Value = "KEYCODE_HOME" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-ch3").ConfigureAwait(false);

    var (text, _) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("mode=KEYCODE_HOME");
  }

  [Fact]
  public async Task OptionValueWithBracesInsideTextReachesTheStepUnchanged() {
    // FR-027: the value "go-{{x}}" is plain text. It is not resolved again, and the step does not fail.
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    var declaration = new ParameterDeclaration {
      Name = "mode", Kind = ParameterInputKind.Choice,
      Options = new[] { new ParameterOption { Label = "Braced", Value = "go-{{x}}" } }
    };
    var bindings = new Collection<ParameterBinding> { new() { Name = "mode", Value = "go-{{x}}" } };
    await SeedAsync(
        services, KeyCommand("c-ch4", "{{mode}}", declaration),
        SequenceInvoking("s-ch4", "c-ch4", bindings), "q-ch4")
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-ch4").ConfigureAwait(false);

    var (text, _) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("mode=go-{{x}}");
  }
}
