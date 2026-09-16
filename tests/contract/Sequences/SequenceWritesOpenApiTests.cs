using System;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Feature 091 (FR-009): the published API description must tell an author that <c>dryRun</c> works on
/// sequence create, update and patch, and that a nonexistent command reference is rejected — a
/// guarantee that lives only in code is one an author learns by breaking a live sequence.
/// </summary>
public sealed class SequenceWritesOpenApiTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<JsonElement> ReadPathsAsync() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.GetProperty("paths").Clone();
  }

  [Theory]
  [InlineData("/api/sequences", "post")]
  [InlineData("/api/sequences/{sequenceId}", "put")]
  [InlineData("/api/sequences/{sequenceId}", "patch")]
  public async Task SequenceWriteOperationDocumentsDryRun(string path, string method) {
    var paths = await ReadPathsAsync().ConfigureAwait(false);

    var description = paths.GetProperty(path).GetProperty(method).GetProperty("description").GetString();

    description.Should().Contain("dryRun");
  }

  [Fact]
  public async Task SequenceUpdateDocumentsNonexistentCommandReferenceRejection() {
    var paths = await ReadPathsAsync().ConfigureAwait(false);

    var description = paths.GetProperty("/api/sequences/{sequenceId}").GetProperty("put").GetProperty("description").GetString();

    description.Should().Contain("commandId").And.Contain("400");
  }
}
