using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Domain.Versioning;
using GameBot.Service.Services.Updates;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdateCheckServiceTests {
  private static UpdateFailureException Failure(string code, string hint = "Try again later.") =>
    new(new UpdateError(code, "message", hint));

  [Fact]
  public async Task NewerReleaseGivesUpdateAvailable() {
    var client = new FakeReleaseClient { Release = UpdateTestData.Release("1.7.0.430") };
    var service = UpdateTestData.CheckService(client, "1.7.0.412");

    var result = await service.CheckAsync(CancellationToken.None).ConfigureAwait(true);

    result.Status.Should().Be(UpdateCheckStatus.UpdateAvailable);
    result.InstalledVersion.Should().Be(new SemanticVersion(1, 7, 0, 412));
    result.LatestVersion.Should().Be(new SemanticVersion(1, 7, 0, 430));
    result.Notes.Should().Be("Notes");
    result.Error.Should().BeNull();
    service.LastCheck.Should().Be(result);
    service.LastRelease.Should().NotBeNull();
  }

  [Fact]
  public async Task EqualReleaseGivesUpToDate() {
    var client = new FakeReleaseClient { Release = UpdateTestData.Release("1.7.0.412") };
    var service = UpdateTestData.CheckService(client, "1.7.0.412");

    var result = await service.CheckAsync(CancellationToken.None).ConfigureAwait(true);

    result.Status.Should().Be(UpdateCheckStatus.UpToDate);
    service.LastRelease.Should().BeNull();
  }

  [Fact]
  public async Task LowerReleaseGivesUpToDateAndNoDowngrade() {
    var client = new FakeReleaseClient { Release = UpdateTestData.Release("1.7.0.400") };
    var service = UpdateTestData.CheckService(client, "1.7.0.412");

    var result = await service.CheckAsync(CancellationToken.None).ConfigureAwait(true);

    result.Status.Should().Be(UpdateCheckStatus.UpToDate);
    result.LatestVersion.Should().Be(new SemanticVersion(1, 7, 0, 400));
    service.LastRelease.Should().BeNull();
  }

  [Theory]
  [InlineData("update_network_error")]
  [InlineData("update_rate_limited")]
  [InlineData("update_no_release")]
  [InlineData("update_manifest_invalid")]
  [InlineData("update_manifest_unsupported")]
  public async Task EveryFailureGivesCheckFailedWithAHint(string code) {
    var client = new FakeReleaseClient { Failure = Failure(code) };
    var service = UpdateTestData.CheckService(client, "1.7.0.412");

    var result = await service.CheckAsync(CancellationToken.None).ConfigureAwait(true);

    result.Status.Should().Be(UpdateCheckStatus.CheckFailed);
    result.Error!.Code.Should().Be(code);
    result.Error.Hint.Should().NotBeNullOrWhiteSpace();
    result.LatestVersion.Should().BeNull();
    service.LastRelease.Should().BeNull();
  }

  [Fact]
  public async Task FailedCheckClearsTheReleaseOfAnEarlierCheck() {
    var client = new FakeReleaseClient { Release = UpdateTestData.Release("1.7.0.430") };
    var service = UpdateTestData.CheckService(client, "1.7.0.412");
    await service.CheckAsync(CancellationToken.None).ConfigureAwait(true);
    client.Failure = Failure("update_network_error");

    await service.CheckAsync(CancellationToken.None).ConfigureAwait(true);

    service.LastRelease.Should().BeNull();
    service.LastCheck!.Status.Should().Be(UpdateCheckStatus.CheckFailed);
  }

  [Fact]
  public void NoCheckMeansNoLastCheck() {
    var service = UpdateTestData.CheckService(new FakeReleaseClient(), "1.7.0.412");

    service.LastCheck.Should().BeNull();
    service.LastRelease.Should().BeNull();
  }
}
