using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Images;

[Collection("ConfigIsolation")]
public sealed class OverwriteImageTests {
  private const string WhitePng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";
  private const string BlackPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mP8/5+hHgAHggJ/PqzZ/QAAAABJRU5ErkJggg==";

  public OverwriteImageTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task PostThenPutReplacesContent() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var create = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "overwrite-me", data = WhitePng });
    create.StatusCode.Should().Be(HttpStatusCode.Created);

    var before = await client.GetByteArrayAsync(new Uri("/api/images/overwrite-me", UriKind.Relative));
    Convert.ToBase64String(before).Should().Be(WhitePng);

    var put = await client.PutAsJsonAsync(new Uri("/api/images/overwrite-me", UriKind.Relative), new { data = BlackPng });
    put.StatusCode.Should().Be(HttpStatusCode.OK);

    var after = await client.GetByteArrayAsync(new Uri("/api/images/overwrite-me", UriKind.Relative));
    Convert.ToBase64String(after).Should().Be(BlackPng);
  }

  // Two valid 8x8 PNG images of the same size (129 bytes) that differ in the pixel color.
  private const string RedPng = "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAWSURBVChTYzjBxfUfH2ZAF0DHw0MBACr7dsGwdpqlAAAAAElFTkSuQmCC";
  private const string BluePng = "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAWSURBVChTY+DiOvEfH2ZAF0DHw0MBAMvsdsFxbQRhAAAAAElFTkSuQmCC";

  [Fact]
  public async Task OverwriteWithSameSizeGivesNewEtagAndBodyForOldEtag() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var create = await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "etag-overwrite", data = RedPng });
    create.StatusCode.Should().Be(HttpStatusCode.Created);

    var first = await client.GetAsync(new Uri("/api/images/etag-overwrite", UriKind.Relative));
    first.StatusCode.Should().Be(HttpStatusCode.OK);
    var oldEtag = first.Headers.ETag;
    oldEtag.Should().NotBeNull();

    var put = await client.PutAsJsonAsync(new Uri("/api/images/etag-overwrite", UriKind.Relative), new { data = BluePng });
    put.StatusCode.Should().Be(HttpStatusCode.OK);

    using var req = new HttpRequestMessage(HttpMethod.Get, new Uri("/api/images/etag-overwrite", UriKind.Relative));
    req.Headers.IfNoneMatch.Add(oldEtag!);
    var second = await client.SendAsync(req);
    second.StatusCode.Should().Be(HttpStatusCode.OK);
    Convert.ToBase64String(await second.Content.ReadAsByteArrayAsync()).Should().Be(BluePng);
    second.Headers.ETag.Should().NotBeNull();
    second.Headers.ETag!.Tag.Should().NotBe(oldEtag!.Tag);
  }
}
