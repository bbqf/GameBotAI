using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Images;

[Collection("ConfigIsolation")]
public sealed class FormUploadTests
{
  private static readonly byte[] TinyPng = Convert.FromHexString("89504E470D0A1A0A");

  public FormUploadTests()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task PostMultipartUploadSucceeds()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    using var content = new MultipartFormDataContent();
    using var idContent = new StringContent("fresh-id");
    content.Add(idContent, "id");

    using var fileContent = new ByteArrayContent(TinyPng);
    fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
    content.Add(fileContent, "file", "tiny.png");

    var response = await client.PostAsync(new Uri("/api/images", UriKind.Relative), content);
    response.StatusCode.Should().Be(HttpStatusCode.Created);

    var saved = await client.GetByteArrayAsync(new Uri("/api/images/fresh-id", UriKind.Relative));
    saved.Should().Equal(TinyPng);
  }
}
