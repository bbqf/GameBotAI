using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

/// <summary>
/// Feature 084: a long run's log is split into linked segments, and each segment ages out on its own.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class ExecutionLogRotationIntegrationTests {
  [Fact]
  public async Task RotatedSegmentsExposeLinksToEachOtherThroughTheDetailEndpoint() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var svc = app.Services.GetRequiredService<IExecutionLogService>();

    var closedId = await svc.LogQueueStartAsync("q-rotate", "Rotating Queue").ConfigureAwait(false);
    var continuationId = await svc.LogQueueRotateAsync(closedId, "q-rotate", "Rotating Queue").ConfigureAwait(false);

    var closedLinks = await ReadRelatedObjectsAsync(client, closedId).ConfigureAwait(false);
    closedLinks.Should().Contain(link =>
      link.TargetId == continuationId && link.Label.Contains("Continues in", StringComparison.Ordinal));

    var continuationLinks = await ReadRelatedObjectsAsync(client, continuationId).ConfigureAwait(false);
    continuationLinks.Should().Contain(link =>
      link.TargetId == closedId && link.Label.Contains("Continued from", StringComparison.Ordinal));
  }

  [Fact] // FR-013: an older segment expires on its own schedule, leaving the live one untouched.
  public async Task RetentionDeletesAClosedSegmentWithoutDisturbingItsContinuation() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var svc = app.Services.GetRequiredService<IExecutionLogService>();
    var repo = app.Services.GetRequiredService<IExecutionLogRepository>();

    var closedId = await svc.LogQueueStartAsync("q-retain", "Aging Queue").ConfigureAwait(false);
    var continuationId = await svc.LogQueueRotateAsync(closedId, "q-retain", "Aging Queue").ConfigureAwait(false);

    // Age only the closed segment past its retention bound.
    var closed = await repo.GetAsync(closedId).ConfigureAwait(false);
    await repo.UpsertAsync(new ExecutionLogEntry {
      Id = closed!.Id,
      TimestampUtc = closed.TimestampUtc,
      ExecutionType = closed.ExecutionType,
      FinalStatus = closed.FinalStatus,
      ObjectRef = closed.ObjectRef,
      Navigation = closed.Navigation,
      Hierarchy = closed.Hierarchy,
      Summary = closed.Summary,
      Details = closed.Details,
      StepOutcomes = closed.StepOutcomes,
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
      RotatedToExecutionId = closed.RotatedToExecutionId,
      RotatedFromExecutionId = closed.RotatedFromExecutionId
    }).ConfigureAwait(false);

    await svc.CleanupExpiredAsync().ConfigureAwait(false);

    (await repo.GetAsync(closedId).ConfigureAwait(false)).Should().BeNull();

    var survivor = await repo.GetAsync(continuationId).ConfigureAwait(false);
    survivor.Should().NotBeNull();
    survivor!.RotatedFromExecutionId.Should().Be(closedId);

    // The surviving segment still serves its detail view, dangling back-link and all.
    var response = await client
      .GetAsync(new Uri($"/api/execution-logs/{continuationId}", UriKind.Relative))
      .ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact] // FR-014: entries written before this feature have no rotation fields at all.
  public async Task EntriesWrittenBeforeRotationExistedStillListAndRender() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var svc = app.Services.GetRequiredService<IExecutionLogService>();
    var repo = app.Services.GetRequiredService<IExecutionLogRepository>();

    var legacy = new ExecutionLogEntry {
      Id = "pre-rotation-1",
      TimestampUtc = DateTimeOffset.UtcNow.AddHours(-2),
      ExecutionType = "queue",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("queue", "q-old", "Old Queue"),
      Navigation = new ExecutionNavigationContext("/queues/q-old", null),
      Hierarchy = new ExecutionHierarchyContext("pre-rotation-1", null, 0, null),
      Summary = "Queue 'Old Queue' completed full run.",
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
    };
    await repo.AddAsync(legacy).ConfigureAwait(false);

    var stored = await svc.GetAsync("pre-rotation-1").ConfigureAwait(false);
    stored!.RotatedToExecutionId.Should().BeNull();
    stored.RotatedFromExecutionId.Should().BeNull();

    var roots = await svc.QueryAsync(new ExecutionLogQuery { RootsOnly = true, PageSize = 50 }).ConfigureAwait(false);
    roots.Items.Should().Contain(i => i.Id == "pre-rotation-1");

    var response = await client
      .GetAsync(new Uri("/api/execution-logs/pre-rotation-1", UriKind.Relative))
      .ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ReadRelatedObjectsAsync(client, "pre-rotation-1").ConfigureAwait(false))
      .Should().NotContain(link => link.Label.Contains("run segment", StringComparison.Ordinal));
  }

  private static async Task<IReadOnlyList<RelatedLink>> ReadRelatedObjectsAsync(HttpClient client, string executionId) {
    var payload = await client.GetFromJsonAsync<JsonElement>($"/api/execution-logs/{executionId}").ConfigureAwait(false);
    return payload.GetProperty("relatedObjects").EnumerateArray()
      .Select(item => new RelatedLink(
        item.GetProperty("label").GetString() ?? string.Empty,
        item.GetProperty("targetId").GetString() ?? string.Empty))
      .ToList();
  }

  private sealed record RelatedLink(string Label, string TargetId);
}
