using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionLogRetentionIntegrationTests {
  [Fact]
  public async Task RetentionPolicyUpdatePersistsAndCleanupDeletesExpiredEntries() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();

    var logService = app.Services.GetRequiredService<IExecutionLogService>();
    var policyRepo = app.Services.GetRequiredService<IExecutionLogRetentionPolicyRepository>();
    var logRepo = app.Services.GetRequiredService<IExecutionLogRepository>();

    var updated = await logService.UpdateRetentionAsync(true, 3, 5).ConfigureAwait(false);
    updated.Enabled.Should().BeTrue();
    updated.RetentionDays.Should().Be(3);
    updated.CleanupIntervalMinutes.Should().Be(5);

    var persisted = await policyRepo.GetAsync().ConfigureAwait(false);
    persisted.Enabled.Should().BeTrue();
    persisted.RetentionDays.Should().Be(3);
    persisted.CleanupIntervalMinutes.Should().Be(5);

    var expired = new ExecutionLogEntry {
      Id = "expired-entry-001",
      TimestampUtc = DateTimeOffset.UtcNow.AddDays(-10),
      ExecutionType = "command",
      FinalStatus = "failure",
      ObjectRef = new ExecutionObjectReference("command", "cmd-expired", "Expired"),
      Navigation = new ExecutionNavigationContext("/authoring/commands/cmd-expired", null),
      Hierarchy = new ExecutionHierarchyContext("root-expired", null, 0, null),
      Summary = "expired",
      Details = Array.Empty<ExecutionDetailItem>(),
      StepOutcomes = Array.Empty<ExecutionStepOutcome>(),
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
    };

    await logRepo.AddAsync(expired).ConfigureAwait(false);

    var deleted = await logService.CleanupExpiredAsync().ConfigureAwait(false);
    deleted.Should().BeGreaterThan(0);

    var stillThere = await logRepo.GetAsync("expired-entry-001").ConfigureAwait(false);
    stillThere.Should().BeNull();
  }
}
