using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>Feature 122: the option is stored in the file, kept by backup and restore, and false for old files.</summary>
[Collection("ConfigIsolation")]
public sealed class ExcludeFromSuccessNotificationsPersistenceTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public ExcludeFromSuccessNotificationsPersistenceTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task TheOptionSurvivesARepositoryReload() {
    var root = Path.Combine(Path.GetTempPath(), $"gamebot-excl-{Guid.NewGuid():N}");
    try {
      var first = new FileSequenceRepository(root);
      await first.CreateAsync(new CommandSequence { Id = "helper", Name = "Helper", ExcludeFromSuccessNotifications = true }).ConfigureAwait(false);
      await first.CreateAsync(new CommandSequence { Id = "plain", Name = "Plain" }).ConfigureAwait(false);

      var second = new FileSequenceRepository(root);

      (await second.GetAsync("helper").ConfigureAwait(false))!.ExcludeFromSuccessNotifications.Should().BeTrue();
      (await second.GetAsync("plain").ConfigureAwait(false))!.ExcludeFromSuccessNotifications.Should().BeFalse();
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task AnOldFileWithNoMemberReadsAsFalse() {
    var root = Path.Combine(Path.GetTempPath(), $"gamebot-excl-{Guid.NewGuid():N}");
    try {
      var repo = new FileSequenceRepository(root);
      var dir = Path.Combine(root, "commands", "sequences");
      await File.WriteAllTextAsync(Path.Combine(dir, "old.json"), "{\"id\":\"old\",\"name\":\"Old\",\"version\":3,\"steps\":[]}").ConfigureAwait(false);

      var loaded = await repo.GetAsync("old").ConfigureAwait(false);

      loaded.Should().NotBeNull();
      loaded!.ExcludeFromSuccessNotifications.Should().BeFalse();
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task BackupExportAndRestoreKeepTheOption() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    var name = "Exclude-Backup-" + Guid.NewGuid().ToString("N");

    var create = await client.PostAsJsonAsync("/api/sequences", new {
      name,
      version = 1,
      excludeFromSuccessNotifications = true,
      steps = new object[] {
        new { stepId = "t1", label = "Tap", stepType = "Action", primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 1 } } }
      }
    }).ConfigureAwait(false);
    create.EnsureSuccessStatusCode();
    var id = (await create.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("id").GetString()!;

    var backup = await client.PostAsJsonAsync("/api/authoring/backup",
      new { commandIds = Array.Empty<string>(), sequenceIds = new[] { id } }).ConfigureAwait(false);
    backup.EnsureSuccessStatusCode();
    var archive = await backup.Content.ReadAsByteArrayAsync().ConfigureAwait(false);

    (await client.DeleteAsync(new Uri($"/api/sequences/{id}", UriKind.Relative)).ConfigureAwait(false)).EnsureSuccessStatusCode();

    using var content = new MultipartFormDataContent();
    using var file = new ByteArrayContent(archive);
    file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
    content.Add(file, "archive", "backup.zip");
    var restore = await client.PostAsync(new Uri("/api/authoring/restore/apply", UriKind.Relative), content).ConfigureAwait(false);
    restore.EnsureSuccessStatusCode();

    var list = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/sequences", UriKind.Relative)).ConfigureAwait(false);
    var restored = list.EnumerateArray().Single(e => e.GetProperty("name").GetString() == name);
    restored.GetProperty("excludeFromSuccessNotifications").GetBoolean().Should().BeTrue();
  }
}
