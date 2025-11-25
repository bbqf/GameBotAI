using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Domain.Services.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Logging;

public sealed class RuntimeLoggingPolicyServiceTests
{
    private static readonly string[] CatalogServiceOnly = { "GameBot.Service" };
    private static readonly string[] CatalogServiceAndTriggers = { "GameBot.Service", "GameBot.Domain.Triggers" };

    private static LoggingPolicySnapshot CreateSnapshot(params LoggingComponentSetting[] components) => new()
    {
        Components = components,
        DefaultLevel = LogLevel.Warning,
        AppliedAtUtc = DateTimeOffset.UtcNow,
        AppliedBy = "seed"
    };

    private static LoggingComponentSetting Component(string name, LogLevel level = LogLevel.Warning, bool enabled = true) => new()
    {
        Name = name,
        EffectiveLevel = level,
        DefaultLevel = LogLevel.Warning,
        Enabled = enabled,
        Source = "tests"
    };

    [Fact]
    public async Task GetSnapshotAsyncLoadsFromRepositoryAndCaches()
    {
        var repo = new InMemoryRepository(CreateSnapshot(Component("GameBot.Service")));
        var applier = new SpyApplier();
        using var service = new RuntimeLoggingPolicyService(repo, CatalogServiceAndTriggers, NullLogger<RuntimeLoggingPolicyService>.Instance, applier);

        var snapshot1 = await service.GetSnapshotAsync().ConfigureAwait(false);
        var snapshot2 = await service.GetSnapshotAsync().ConfigureAwait(false);

        snapshot1.Should().NotBeSameAs(snapshot2);
        snapshot1.Components.Should().HaveCount(2, "missing catalog entries are appended");
        applier.ApplyAllInvocations.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task SetComponentAsyncPersistsChangesAndNotifies()
    {
        var repo = new InMemoryRepository(CreateSnapshot(Component("GameBot.Service")));
        var applier = new SpyApplier();
        var logger = new TestLogger<RuntimeLoggingPolicyService>();
        using var service = new RuntimeLoggingPolicyService(repo, CatalogServiceOnly, logger, applier);
        LoggingChangeAudit? recordedAudit = null;
        service.AuditRecorded += (_, args) => recordedAudit = args.Audit;

        var updated = await service.SetComponentAsync("GameBot.Service", LogLevel.Debug, null, "tester", "raise level").ConfigureAwait(false);

        repo.Snapshot.Components.Single().EffectiveLevel.Should().Be(LogLevel.Debug);
        applier.LastComponentApplied.Should().NotBeNull();
        applier.LastComponentApplied!.EffectiveLevel.Should().Be(LogLevel.Debug);
        recordedAudit.Should().NotBeNull();
        recordedAudit!.Component.Should().Be("GameBot.Service");
        logger.StateMessages.Should().Contain(s => s.Contains("Logging policy change"));
        updated.LastChangedBy.Should().Be("tester");
    }

    [Fact]
    public async Task ResetAsyncRevertsAllComponentsToDefaults()
    {
        var repo = new InMemoryRepository(CreateSnapshot(
            Component("GameBot.Service", LogLevel.Error, enabled: false),
            Component("GameBot.Domain.Triggers", LogLevel.Information, enabled: true)));
        var applier = new SpyApplier();
        using var service = new RuntimeLoggingPolicyService(repo, CatalogServiceAndTriggers, NullLogger<RuntimeLoggingPolicyService>.Instance, applier);

        var snapshot = await service.ResetAsync("ops", "maintenance").ConfigureAwait(false);

        snapshot.Components.Should().OnlyContain(c => c.Enabled && c.EffectiveLevel == c.DefaultLevel);
        applier.ApplyAllInvocations.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SetComponentAsyncThrowsForUnknownComponent()
    {
        var repo = new InMemoryRepository(CreateSnapshot(Component("GameBot.Service")));
        using var service = new RuntimeLoggingPolicyService(repo, CatalogServiceOnly, NullLogger<RuntimeLoggingPolicyService>.Instance);

        var act = () => service.SetComponentAsync("Other", LogLevel.Trace, null, "tester", null);

        await act.Should().ThrowAsync<KeyNotFoundException>().ConfigureAwait(false);
    }

    private sealed class InMemoryRepository : ILoggingPolicyRepository
    {
        public InMemoryRepository(LoggingPolicySnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public LoggingPolicySnapshot Snapshot { get; private set; }

        public Task<LoggingPolicySnapshot> LoadAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Snapshot.DeepClone());
        }

        public Task SaveAsync(LoggingPolicySnapshot snapshot, CancellationToken ct = default)
        {
            Snapshot = snapshot.DeepClone();
            return Task.CompletedTask;
        }
    }

    private sealed class SpyApplier : ILoggingPolicyApplier
    {
        public int ApplyAllInvocations { get; private set; }
        public LoggingComponentSetting? LastComponentApplied { get; private set; }

        public void ApplyComponent(LoggingComponentSetting component)
        {
            LastComponentApplied = component;
        }

        public void ApplyAll(IEnumerable<LoggingComponentSetting> components)
        {
            ApplyAllInvocations++;
        }
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> StateMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            StateMessages.Add(formatter(state, exception));
        }
    }
}
