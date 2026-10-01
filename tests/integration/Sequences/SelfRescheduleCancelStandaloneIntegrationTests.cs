using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>Feature 123 (SC-003): a sequence with only a Cancel step runs outside any queue and succeeds.</summary>
[Collection("ConfigIsolation")]
public sealed class SelfRescheduleCancelStandaloneIntegrationTests {
  public SelfRescheduleCancelStandaloneIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task CancelOnlySequenceSucceedsOutsideAQueue() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    var sequence = new CommandSequence { Id = "seq-cancel-standalone-only", Name = "Cancel only" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0, StepId = "cancel", StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload {
          Type = ActionTypes.RescheduleSelf,
          Parameters = { ["option"] = "Cancel" }
        }
      }
    });
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);

    var result = await services.GetRequiredService<ISequenceExecutionService>()
      .ExecuteAsync("seq-cancel-standalone-only", sessionId: null, parentContext: null).ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    var step = result.Steps.Single();
    step.ActionOutcome.Should().Be("noop");
    step.Removed.Should().BeFalse();
  }
}
