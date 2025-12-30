using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameBot.Domain.Triggers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Images;

[Collection("ConfigIsolation")]
public sealed class DeleteImageIntegrationTests
{
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public DeleteImageIntegrationTests()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task DeleteReferencedReturnsConflict()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "img-ref", data = OneByOnePngBase64 });

    var dataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR") ?? throw new InvalidOperationException();
    var triggers = new GameBot.Domain.Triggers.FileTriggerRepository(dataDir);
    await triggers.UpsertAsync(new Trigger
    {
      Id = "blocker",
      Type = TriggerType.ImageMatch,
      Params = new ImageMatchParams
      {
        ReferenceImageId = "img-ref",
        Region = new Region { X = 0, Y = 0, Width = 1, Height = 1 },
        SimilarityThreshold = 0.9
      }
    });

    var resp = await client.DeleteAsync(new Uri("/api/images/img-ref", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var body = await resp.Content.ReadFromJsonAsync<ConflictBody>();
    body.Should().NotBeNull();
    body!.Error.BlockingTriggerIds.Should().Contain("blocker");
  }

  [Fact]
  public async Task DeleteUnreferencedRemovesImage()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "img-free", data = OneByOnePngBase64 });

    var resp = await client.DeleteAsync(new Uri("/api/images/img-free", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var check = await client.GetAsync(new Uri("/api/images/img-free", UriKind.Relative));
    check.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  private sealed record ConflictBody(ConflictError Error);
  private sealed record ConflictError(string Code, string Message, string[] BlockingTriggerIds);
}
