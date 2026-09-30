using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.UnitTests.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.IntegrationTests.Notifications;

/// <summary>Feature 120 (V-21, FR-018): a hand edit of the target file applies with no restart.</summary>
[Collection("ConfigIsolation")]
public sealed class NotificationStoreReloadTests {
  private readonly string _dataDir;

  public NotificationStoreReloadTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    _dataDir = TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task V21_ANewFileAppearsWithinTwoSecondsAndACorruptFileKeepsTheLastGoodList() {
    using var app = NotificationIntegrationHelpers.HostWithChannel(new RecordingChannel());
    _ = app.CreateClient();
    var store = app.Services.GetRequiredService<INotificationTargetStore>();
    store.List().Should().BeEmpty();
    var path = Path.Combine(_dataDir, "notifications", "targets.json");

    await File.WriteAllTextAsync(path,
      "[{\"id\":\"hand1\",\"type\":\"telegram\",\"name\":\"Hand\",\"enabled\":true,\"settings\":{\"chatId\":\"7\",\"botToken\":\"123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ\"}}]");
    await NotificationIntegrationHelpers.WaitForAsync(() => store.List().Count == 1, 2000);

    store.List().Select(t => t.Id).Should().Equal("hand1");

    await File.WriteAllTextAsync(path, "{ broken");
    await Task.Delay(100);
    store.List().Select(t => t.Id).Should().Equal("hand1");
  }
}
