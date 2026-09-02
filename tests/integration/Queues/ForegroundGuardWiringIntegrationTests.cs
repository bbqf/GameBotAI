using System.Linq;
using System.Reflection;
using FluentAssertions;
using GameBot.Service.Services.EnsureGameRunning;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// The foreground guard is an OPTIONAL constructor dependency of <see cref="QueueExecutionService"/>,
/// so a missing registration would not fail the build or any behavioral test — the queue would simply
/// go back to running for hours without ever noticing the game had left the foreground. These tests
/// pin the wiring itself.
/// </summary>
public sealed class ForegroundGuardWiringIntegrationTests {
  [Fact]
  public void ContainerResolvesTheForegroundGuard() {
    using var app = new WebApplicationFactory<Program>();

    var guard = app.Services.GetRequiredService<IGameForegroundGuard>();

    guard.Should().BeOfType<GameForegroundGuard>();
  }

  [Fact]
  public void QueueExecutionServiceReceivesTheForegroundGuard() {
    using var app = new WebApplicationFactory<Program>();

    var queueExecution = app.Services.GetRequiredService<IQueueExecutionService>();

    // Optional ctor params are filled from the container when registered and fall back to their
    // default (null) when not, so the injected field is the only honest evidence the guard is live.
    var field = typeof(QueueExecutionService)
      .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
      .Single(f => f.FieldType == typeof(IGameForegroundGuard));

    field.GetValue(queueExecution).Should().NotBeNull("a queue run must confirm the game is in front before each firing");
  }
}
