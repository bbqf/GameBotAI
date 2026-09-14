using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

/// <summary>
/// Feature 084: a queue run open longer than 24h is closed out and continued in a new run segment,
/// with each side of the cut carrying a structured link to the other.
/// </summary>
public sealed class ExecutionLogRotationTests {
  [Fact]
  public async Task RotationClosesTheCurrentSegmentAndOpensALinkedContinuation() {
    var (service, repository, retention) = CreateService();
    using var _ = repository;
    using var __ = retention;

    var rootId = await service.LogQueueStartAsync("q1", "Daily Queue").ConfigureAwait(false);
    var continuationId = await service.LogQueueRotateAsync(rootId, "q1", "Daily Queue").ConfigureAwait(false);

    continuationId.Should().NotBe(rootId);

    var closed = await service.GetAsync(rootId).ConfigureAwait(false);
    closed.Should().NotBeNull();
    closed!.RotatedToExecutionId.Should().Be(continuationId);
    closed.RotatedFromExecutionId.Should().BeNull();
    // Terminal, so the closed segment does not dangle as forever-"running".
    closed.FinalStatus.Should().Be("success");
    closed.Summary.Should().Contain("rotated");
    // The rotation marker is the segment's last entry.
    closed.Details.Should().NotBeEmpty();
    closed.Details[^1].Kind.Should().Be("rotation");
    closed.Details[^1].Message.Should().Contain(continuationId);

    var continuation = await service.GetAsync(continuationId).ConfigureAwait(false);
    continuation.Should().NotBeNull();
    continuation!.RotatedFromExecutionId.Should().Be(rootId);
    continuation.RotatedToExecutionId.Should().BeNull();
    continuation.FinalStatus.Should().Be("running");
    continuation.Summary.Should().Contain("continuation");
    // ...and the continuation marker is the new segment's first entry.
    continuation.Details.Should().NotBeEmpty();
    continuation.Details[0].Kind.Should().Be("rotation");
    continuation.Details[0].Message.Should().Contain(rootId);
  }

  [Fact]
  public async Task ASegmentRotatedTwiceKeepsBothEndsOfTheChain() {
    var (service, repository, retention) = CreateService();
    using var _ = repository;
    using var __ = retention;

    var first = await service.LogQueueStartAsync("q1", "Daily Queue").ConfigureAwait(false);
    var second = await service.LogQueueRotateAsync(first, "q1", "Daily Queue").ConfigureAwait(false);
    var third = await service.LogQueueRotateAsync(second, "q1", "Daily Queue").ConfigureAwait(false);

    var middle = await service.GetAsync(second).ConfigureAwait(false);
    middle.Should().NotBeNull();
    middle!.RotatedFromExecutionId.Should().Be(first);
    middle.RotatedToExecutionId.Should().Be(third);
  }

  [Fact]
  public async Task RotationLinksAreSurfacedAsRelatedObjectsOnBothSegments() {
    var (service, repository, retention) = CreateService();
    using var _ = repository;
    using var __ = retention;

    var rootId = await service.LogQueueStartAsync("q1", "Daily Queue").ConfigureAwait(false);
    var continuationId = await service.LogQueueRotateAsync(rootId, "q1", "Daily Queue").ConfigureAwait(false);

    var closed = await service.GetAsync(rootId).ConfigureAwait(false);
    var closedProjection = ExecutionLogService.BuildDetailProjection(closed!);
    closedProjection.RelatedObjects.Should().ContainSingle(r => r.TargetId == continuationId)
      .Which.Label.Should().Contain("Continues in");

    var continuation = await service.GetAsync(continuationId).ConfigureAwait(false);
    var continuationProjection = ExecutionLogService.BuildDetailProjection(continuation!);
    continuationProjection.RelatedObjects.Should().ContainSingle(r => r.TargetId == rootId)
      .Which.Label.Should().Contain("Continued from");
  }

  [Fact]
  public async Task AnUnrotatedQueueRunCarriesNoRotationLinks() {
    var (service, repository, retention) = CreateService();
    using var _ = repository;
    using var __ = retention;

    var rootId = await service.LogQueueStartAsync("q1", "Daily Queue").ConfigureAwait(false);

    var entry = await service.GetAsync(rootId).ConfigureAwait(false);
    entry!.RotatedToExecutionId.Should().BeNull();
    entry.RotatedFromExecutionId.Should().BeNull();
    ExecutionLogService.BuildDetailProjection(entry).RelatedObjects
      .Should().NotContain(r => r.Label.Contains("run segment", StringComparison.Ordinal));
  }

  private static (ExecutionLogService Service, FileExecutionLogRepository Repository, ExecutionLogRetentionPolicyRepository Retention) CreateService() {
    var storageRoot = Path.Combine(Path.GetTempPath(), "GameBot.UnitTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(storageRoot);
    var repository = new FileExecutionLogRepository(storageRoot);
    var retention = new ExecutionLogRetentionPolicyRepository(storageRoot);
    return (new ExecutionLogService(repository, retention), repository, retention);
  }
}
