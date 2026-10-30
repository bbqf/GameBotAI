#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GameBot.IntegrationTests.Queues.FriendlyRunHelpers;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 168: the sequence declares the choice and the command has a plain declaration of the same
/// name. The log still shows the option label of the sequence.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class FriendlyChoicePassThroughLabelRunTests {
  public FriendlyChoicePassThroughLabelRunTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task ChoiceLabelOfTheSequenceReachesTheLogThroughAPlainCommandDeclaration() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    var command = new Command { Id = "c-passlabel", Name = "Swipe" };
    command.Parameters.Add(new ParameterDeclaration { Name = "swipeMs", Type = ParameterValueType.Number });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 0,
      Swipe = new SwipeConfig { StartX = 1, StartY = 2, EndX = 3, EndY = 4 },
      FieldTemplates = new Dictionary<string, string> { ["swipe.durationMs"] = "{{swipeMs}}" }
    });

    var difficulty = new ParameterDeclaration {
      Name = "swipeMs", Type = ParameterValueType.Number, Kind = ParameterInputKind.Choice,
      Options = new[] {
        new ParameterOption { Label = "Easy", Value = "100" },
        new ParameterOption { Label = "Hard mode", Value = "300" }
      }
    };
    var bindings = new Collection<ParameterBinding> { new() { Name = "swipeMs", Value = "{{swipeMs}}" } };

    await SeedAsync(
        services, command, SequenceInvoking("s-passlabel", "c-passlabel", bindings, difficulty), "q-passlabel",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "swipeMs", Value = "100" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-passlabel").ConfigureAwait(false);

    var (text, data) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("swipeMs=100 (Easy)");
    data.Should().Contain("\"label\":\"Easy\"");
  }

  [Fact]
  public async Task TreeStepNodeHasTheParameterValueAndTheLabel() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    var command = new Command { Id = "c-treeparam", Name = "Swipe" };
    command.Parameters.Add(new ParameterDeclaration { Name = "swipeMs", Type = ParameterValueType.Number });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.Swipe,
      Order = 0,
      Swipe = new SwipeConfig { StartX = 1, StartY = 2, EndX = 3, EndY = 4 },
      FieldTemplates = new Dictionary<string, string> { ["swipe.durationMs"] = "{{swipeMs}}" }
    });
    var choice = new ParameterDeclaration {
      Name = "swipeMs", Type = ParameterValueType.Number, Kind = ParameterInputKind.Choice,
      Options = new[] { new ParameterOption { Label = "Hard mode", Value = "300" } }
    };
    var bindings = new Collection<ParameterBinding> { new() { Name = "swipeMs", Value = "{{swipeMs}}" } };

    await SeedAsync(
        services, command, SequenceInvoking("s-treeparam", "c-treeparam", bindings, choice), "q-treeparam",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "swipeMs", Value = "300" }))
      .ConfigureAwait(false);
    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-treeparam").ConfigureAwait(false);

    var log = services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "queue", PageSize = 20 }).ConfigureAwait(false);
    var root = page.Items.Should().ContainSingle().Subject;
    var subtree = await log.GetSubtreeAsync(root.Id).ConfigureAwait(false);

    var parameters = new List<ExecutionParameterProjection>();
    CollectParameters(subtree!.Root, parameters);
    parameters.Should().ContainSingle(p => p.Name == "swipeMs" && p.Value == "300" && p.Label == "Hard mode");
  }

  private static void CollectParameters(ExecutionTreeNodeProjection node, List<ExecutionParameterProjection> into) {
    if (node.Parameters is not null) into.AddRange(node.Parameters);
    foreach (var child in node.Children) CollectParameters(child, into);
  }
}
