using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Domain.Services.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Logging;

public sealed class LoggingPolicyRepositoryTests : IDisposable
{
    private readonly string _storageRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private static readonly string[] DefaultComponents = { "GameBot.Service" };

    private static LoggingPolicySnapshot CreateDefaultSnapshot() =>
        LoggingPolicySnapshot.CreateDefault(DefaultComponents, LogLevel.Warning, "tests");

    [Fact]
    public async Task LoadAsyncReturnsDefaultWhenFileMissing()
    {
        using var repo = new LoggingPolicyRepository(_storageRoot, CreateDefaultSnapshot, NullLogger<LoggingPolicyRepository>.Instance);
        var snapshot = await repo.LoadAsync().ConfigureAwait(false);

        snapshot.Components.Should().HaveCount(1);
        snapshot.Components[0].Name.Should().Be("GameBot.Service");
    }

    [Fact]
    public async Task SaveAsyncWritesSnapshotThatCanBeReloaded()
    {
        using var repo = new LoggingPolicyRepository(_storageRoot, CreateDefaultSnapshot, NullLogger<LoggingPolicyRepository>.Instance);
        var original = CreateDefaultSnapshot() with
        {
            AppliedBy = "tester",
            Components = new[]
            {
                new LoggingComponentSetting
                {
                    Name = "GameBot.Service",
                    Enabled = false,
                    EffectiveLevel = LogLevel.Error,
                    DefaultLevel = LogLevel.Warning,
                    Source = "tests",
                    LastChangedBy = "tester",
                    LastChangedAtUtc = DateTimeOffset.UtcNow
                }
            }
        };

        await repo.SaveAsync(original).ConfigureAwait(false);
        var reloaded = await repo.LoadAsync().ConfigureAwait(false);

        reloaded.Should().BeEquivalentTo(original, opts => opts
            .Excluding(s => s.Components[0].LastChangedAtUtc));
        reloaded.Components[0].LastChangedAtUtc.Should().BeCloseTo(original.Components[0].LastChangedAtUtc!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LoadAsyncInvalidJsonFallsBackToDefault()
    {
        var configDir = Path.Combine(_storageRoot, "config");
        Directory.CreateDirectory(configDir);
        var filePath = Path.Combine(configDir, "logging-policy.json");
        await File.WriteAllTextAsync(filePath, "{ not json }").ConfigureAwait(false);

        using var repo = new LoggingPolicyRepository(_storageRoot, CreateDefaultSnapshot, NullLogger<LoggingPolicyRepository>.Instance);
        var snapshot = await repo.LoadAsync().ConfigureAwait(false);

        snapshot.Components.Should().HaveCount(1);
        snapshot.Components[0].Name.Should().Be("GameBot.Service");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
        catch
        {
            // swallow cleanup exceptions
        }
    }
}
