using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.IntegrationTests.Notifications;

/// <summary>Feature 120 (R-009): the author-data backup never holds the target file or a token.</summary>
[Collection("ConfigIsolation")]
public sealed class NotificationBackupExclusionTests {
  public NotificationBackupExclusionTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task TheBackupArchiveHasNoTargetsFileAndNoToken() {
    using var app = NotificationIntegrationHelpers.HostWithChannel(new RecordingChannel());
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var target = NotificationIntegrationHelpers.AddTarget(app.Services);
    await NotificationIntegrationHelpers.SeedSequenceAsync(app.Services, "s-backup", stepCount: 1);

    var response = await client.PostAsJsonAsync(new Uri("/api/authoring/backup", UriKind.Relative),
      new { commandIds = Array.Empty<string>(), sequenceIds = new[] { "s-backup" } });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var bytes = await response.Content.ReadAsByteArrayAsync();
    using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    archive.Entries.Should().NotBeEmpty();
    archive.Entries.Select(e => e.FullName).Should().NotContain(n => n.Contains("targets", StringComparison.OrdinalIgnoreCase) || n.Contains("notification", StringComparison.OrdinalIgnoreCase));
    foreach (var entry in archive.Entries) {
      using var reader = new StreamReader(entry.Open());
      var text = await reader.ReadToEndAsync();
      text.Should().NotContain(target.Settings["botToken"]);
    }
  }
}
