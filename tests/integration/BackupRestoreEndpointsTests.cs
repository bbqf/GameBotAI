using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests;

[Collection("ConfigIsolation")]
public sealed class BackupRestoreEndpointsTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;

  public BackupRestoreEndpointsTests() {
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

  // ────────────── T020: POST /api/authoring/backup ──────────────

  [Fact(DisplayName = "Backup: empty selection returns 400")]
  public async Task BackupEmptySelectionReturnsBadRequest() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    var response = await client.PostAsJsonAsync("/api/authoring/backup",
      new { commandIds = Array.Empty<string>(), sequenceIds = Array.Empty<string>() }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact(DisplayName = "Backup: selected command produces zip archive")]
  public async Task BackupSelectedCommandProducesZip() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    var cmdId = await CreateCommandAsync(client, "cmd-for-backup").ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/authoring/backup",
      new { commandIds = new[] { cmdId }, sequenceIds = Array.Empty<string>() }).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    response.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");

    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    archive.GetEntry("manifest.json").Should().NotBeNull();
    archive.Entries.Any(e => e.FullName.StartsWith("commands/", StringComparison.Ordinal)).Should().BeTrue();
  }

  // ────────────── T029: DryRunRestore — no conflicts ──────────────

  [Fact(DisplayName = "DryRunRestore: archive with no conflicts returns no-conflict report")]
  public async Task DryRunRestoreNoConflictsReturnsReport() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    var cmdId = await CreateCommandAsync(client, "cmd-dry-run-unique").ConfigureAwait(false);
    var archive = await DownloadBackupAsync(client, new[] { cmdId }, Array.Empty<string>()).ConfigureAwait(false);

    // delete the command so there is no name conflict on a fresh restore
    await client.DeleteAsync(new Uri($"/api/commands/{cmdId}", UriKind.Relative)).ConfigureAwait(false);

    var report = await PostDryRunAsync(client, archive).ConfigureAwait(false);

    report.Should().NotBeNull();
    var reportEl1 = report!.Value;
    reportEl1.TryGetProperty("hasConflicts", out var hc).Should().BeTrue();
    hc.GetBoolean().Should().BeFalse();
    reportEl1.TryGetProperty("totalCommands", out var tc).Should().BeTrue();
    tc.GetInt32().Should().Be(1);
  }

  // ────────────── T030: DryRunRestore — name conflict ──────────────

  [Fact(DisplayName = "DryRunRestore: archive with name-conflicting command returns conflict report")]
  public async Task DryRunRestoreNameConflictReturnsConflictReport() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    var cmdId = await CreateCommandAsync(client, "cmd-conflict-dry").ConfigureAwait(false);
    var archive = await DownloadBackupAsync(client, new[] { cmdId }, Array.Empty<string>()).ConfigureAwait(false);

    // Command with same name still exists → conflict
    var report = await PostDryRunAsync(client, archive).ConfigureAwait(false);

    report.Should().NotBeNull();
    var reportEl2 = report!.Value;
    reportEl2.TryGetProperty("hasConflicts", out var hc).Should().BeTrue();
    hc.GetBoolean().Should().BeTrue();
    reportEl2.TryGetProperty("conflictingCommandNames", out var names).Should().BeTrue();
    names.EnumerateArray().Select(n => n.GetString()).Should().Contain("cmd-conflict-dry");
  }

  // ────────────── T031: ApplyRestore — no conflict ──────────────

  [Fact(DisplayName = "ApplyRestore: restoring archive with no conflicts creates objects")]
  public async Task ApplyRestoreNoConflictCreatesObjects() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    var cmdId = await CreateCommandAsync(client, "cmd-apply-unique").ConfigureAwait(false);
    var archive = await DownloadBackupAsync(client, new[] { cmdId }, Array.Empty<string>()).ConfigureAwait(false);

    // Delete the command so restoring has no conflict and creates anew
    await client.DeleteAsync(new Uri($"/api/commands/{cmdId}", UriKind.Relative)).ConfigureAwait(false);

    var result = await PostApplyAsync(client, archive).ConfigureAwait(false);

    result.Should().NotBeNull();
    var resultEl1 = result!.Value;
    resultEl1.TryGetProperty("restoredCommands", out var rc).Should().BeTrue();
    rc.GetInt32().Should().Be(1);
    resultEl1.TryGetProperty("rolledBack", out var rb).Should().BeTrue();
    rb.GetBoolean().Should().BeFalse();
  }

  // ────────────── T037: ApplyRestore — with conflict ──────────────

  [Fact(DisplayName = "ApplyRestore: restoring archive with conflicting command overwrites it")]
  public async Task ApplyRestoreConflictOverwritesExistingCommand() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    // Create command, back it up, then re-create the same-named command to produce a conflict
    var originalId = await CreateCommandAsync(client, "cmd-overwrite-target").ConfigureAwait(false);
    var archive = await DownloadBackupAsync(client, new[] { originalId }, Array.Empty<string>()).ConfigureAwait(false);

    // Delete and re-create to give it a different ID — same name, different content
    await client.DeleteAsync(new Uri($"/api/commands/{originalId}", UriKind.Relative)).ConfigureAwait(false);
    await CreateCommandAsync(client, "cmd-overwrite-target").ConfigureAwait(false);

    // Dry-run should show conflict
    var dryRun = await PostDryRunAsync(client, archive).ConfigureAwait(false);
    dryRun.Should().NotBeNull();
    dryRun!.Value.GetProperty("hasConflicts").GetBoolean().Should().BeTrue();

    // Apply should succeed: overwrite the conflicting command
    var result = await PostApplyAsync(client, archive).ConfigureAwait(false);
    result.Should().NotBeNull();
    var resultEl2 = result!.Value;
    resultEl2.GetProperty("rolledBack").GetBoolean().Should().BeFalse();
    resultEl2.GetProperty("restoredCommands").GetInt32().Should().Be(1);
  }

  // ────────────── T040: 400 edge cases ──────────────

  [Fact(DisplayName = "DryRunRestore: corrupt zip file returns 400")]
  public async Task DryRunRestoreCorruptZipReturnsBadRequest() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    var corrupt = new byte[] { 0x00, 0xFF, 0xAB, 0xCD };
    using var content = new MultipartFormDataContent();
    using var fileContent = new ByteArrayContent(corrupt);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
    content.Add(fileContent, "archive", "corrupt.zip");

    var response = await client.PostAsync(new Uri("/api/authoring/restore/dry-run", UriKind.Relative), content).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact(DisplayName = "ApplyRestore: corrupt zip file returns 400")]
  public async Task ApplyRestoreCorruptZipReturnsBadRequest() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    var corrupt = new byte[] { 0x00, 0xFF, 0xAB, 0xCD };
    using var content = new MultipartFormDataContent();
    using var fileContent = new ByteArrayContent(corrupt);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
    content.Add(fileContent, "archive", "corrupt.zip");

    var response = await client.PostAsync(new Uri("/api/authoring/restore/apply", UriKind.Relative), content).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact(DisplayName = "DryRunRestore: missing archive file returns 400")]
  public async Task DryRunRestoreMissingFileReturnsBadRequest() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    // POST with no multipart body → no form content type → 400
    var response = await client.PostAsync(new Uri("/api/authoring/restore/dry-run", UriKind.Relative), null).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact(DisplayName = "ApplyRestore: missing archive file returns 400")]
  public async Task ApplyRestoreMissingFileReturnsBadRequest() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorize(app.CreateClient());

    // POST with no multipart body → no form content type → 400
    var response = await client.PostAsync(new Uri("/api/authoring/restore/apply", UriKind.Relative), null).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  // ────────────── helpers ──────────────

  private static HttpClient Authorize(HttpClient client) {
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    return client;
  }

  private static async Task<string> CreateCommandAsync(HttpClient client, string name) {
    var resp = await client.PostAsJsonAsync("/api/commands", new {
      name,
      steps = Array.Empty<object>()
    }).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    var json = await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return json.GetProperty("id").GetString()!;
  }

  private static async Task<byte[]> DownloadBackupAsync(HttpClient client, string[] commandIds, string[] sequenceIds) {
    var resp = await client.PostAsJsonAsync("/api/authoring/backup",
      new { commandIds, sequenceIds }).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
  }

  private static async Task<JsonElement?> PostDryRunAsync(HttpClient client, byte[] archive) {
    using var content = new MultipartFormDataContent();
    using var fileContent = new ByteArrayContent(archive);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
    content.Add(fileContent, "archive", "backup.zip");
    var resp = await client.PostAsync(new Uri("/api/authoring/restore/dry-run", UriKind.Relative), content).ConfigureAwait(false);
    if (!resp.IsSuccessStatusCode) return null;
    return await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
  }

  private static async Task<JsonElement?> PostApplyAsync(HttpClient client, byte[] archive) {
    using var content = new MultipartFormDataContent();
    using var fileContent = new ByteArrayContent(archive);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
    content.Add(fileContent, "archive", "backup.zip");
    var resp = await client.PostAsync(new Uri("/api/authoring/restore/apply", UriKind.Relative), content).ConfigureAwait(false);
    if (!resp.IsSuccessStatusCode && (int)resp.StatusCode != 500) return null;
    return await resp.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
  }
}
