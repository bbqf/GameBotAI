using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GameBot.Domain.Triggers;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Images;

public sealed class DeleteImageTests
{
  private const string OneByOnePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public DeleteImageTests()
  {
    var tmp = Path.Combine(Path.GetTempPath(), "GameBotContract", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tmp);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", tmp);
    Environment.SetEnvironmentVariable("Service__Storage__Root", tmp);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
  }

  [Fact]
  public async Task DeleteReferencedImageReturnsConflict()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var create = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl", data = OneByOnePngBase64 });
    create.StatusCode.Should().Be(HttpStatusCode.Created);

    var dataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR") ?? throw new InvalidOperationException();
    var triggers = new FileTriggerRepository(dataDir);
    await triggers.UpsertAsync(new Trigger
    {
      Id = "t1",
      Type = TriggerType.ImageMatch,
      Params = new ImageMatchParams
      {
        ReferenceImageId = "tpl",
        Region = new Region { X = 0, Y = 0, Width = 1, Height = 1 },
        SimilarityThreshold = 0.85
      }
    });

    var resp = await client.DeleteAsync(new Uri("/api/images/tpl", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var payload = await resp.Content.ReadFromJsonAsync<ConflictBody>();
    payload.Should().NotBeNull();
    payload!.Error.Code.Should().Be("conflict");
    payload.Error.BlockingTriggerIds.Should().Contain("t1");
  }

  [Fact]
  public async Task DeleteUnreferencedImageSucceeds()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var create = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "tpl2", data = OneByOnePngBase64 });
    create.StatusCode.Should().Be(HttpStatusCode.Created);

    var resp = await client.DeleteAsync(new Uri("/api/images/tpl2", UriKind.Relative));
    resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

    var notFound = await client.GetAsync(new Uri("/api/images/tpl2", UriKind.Relative));
    notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  private sealed record ConflictBody(ConflictError Error);
  private sealed record ConflictError(string Code, string Message, string[] BlockingTriggerIds);
}
