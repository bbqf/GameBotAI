using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2000

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 170 (FR-006): the API and the repository give one error text for an unknown <c>onTimeLimit</c>.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class OnTimeLimitSharedMessageTests : OnTimeLimitIntegrationTestBase {
  [Fact]
  public async Task TheApiAndTheRepositoryGiveTheSameMessage() {
    using var app = new WebApplicationFactory<Program>();
    var client = CreateClient(app);
    var json = "{\"name\":\"OnLimit-Shared-" + Guid.NewGuid().ToString("N") + "\",\"version\":1,\"onTimeLimit\":\"stop\",\"steps\":[{\"stepId\":\"t1\",\"label\":\"Tap\",\"stepType\":\"Action\","
      + "\"primitiveAction\":{\"type\":\"tap\",\"schemaVersion\":\"v1\",\"payload\":{\"x\":1,\"y\":1}}}]}";
    using var content = new StringContent(json, Encoding.UTF8, "application/json");

    var response = await client.PostAsync(new Uri("/api/sequences", UriKind.Relative), content).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    var apiMessage = body.GetProperty("errors")[0].GetString();

    var root = Path.Combine(Path.GetTempPath(), $"gamebot-shared-{Guid.NewGuid():N}");
    try {
      var repo = new FileSequenceRepository(root);
      var act = async () => await repo.CreateAsync(new CommandSequence { Id = "x", Name = "x", OnTimeLimit = "stop" }).ConfigureAwait(false);
      var thrown = await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
      var repoMessage = thrown.Which.Message;

      apiMessage.Should().Be(SequenceTimeLimits.OnTimeLimitInvalidMessage);
      repoMessage.Should().Be(SequenceTimeLimits.OnTimeLimitInvalidMessage);
      apiMessage.Should().Be(repoMessage);
    }
    finally {
      try { Directory.Delete(root, recursive: true); }
      catch (IOException) { /* best effort */ }
    }
  }
}
