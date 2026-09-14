using FluentAssertions;
using GameBot.Domain.Logging;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

/// <summary>
/// Feature 084: execution logs are kept for a week by default, so a deployment nobody configured
/// does not accumulate two months of history.
/// </summary>
public sealed class ExecutionLogRetentionPolicyDefaultsTests {
  [Fact]
  public void DefaultRetentionIsOneWeek() {
    ExecutionLogRetentionPolicy.Default.RetentionDays.Should().Be(7);
    new ExecutionLogRetentionPolicy().RetentionDays.Should().Be(7);
  }

  [Fact]
  public void OtherRetentionDefaultsAreUnchanged() {
    var policy = ExecutionLogRetentionPolicy.Default;
    policy.Enabled.Should().BeTrue();
    policy.CleanupIntervalMinutes.Should().Be(30);
  }

  [Fact]
  public async Task AnExplicitlySavedRetentionValueIsNotOverwrittenByTheDefault() {
    var storageRoot = Path.Combine(Path.GetTempPath(), "GameBot.UnitTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(storageRoot);
    using var repository = new ExecutionLogRetentionPolicyRepository(storageRoot);

    // No policy saved yet → the new default applies.
    var initial = await repository.GetAsync().ConfigureAwait(false);
    initial.RetentionDays.Should().Be(7);

    await repository.SaveAsync(new ExecutionLogRetentionPolicy { RetentionDays = 45 }).ConfigureAwait(false);

    // A fresh repository over the same storage reads back the saved value, not the default.
    using var reopened = new ExecutionLogRetentionPolicyRepository(storageRoot);
    var saved = await reopened.GetAsync().ConfigureAwait(false);
    saved.RetentionDays.Should().Be(45);
  }
}
