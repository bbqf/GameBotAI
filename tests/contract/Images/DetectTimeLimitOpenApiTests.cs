using System;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Images;

/// <summary>
/// Issue #223: the OpenAPI document of <c>POST /api/images/detect</c> describes the
/// <c>504 detection_timeout</c> failure and the meaning of <c>limitsHit</c>.
/// </summary>
public sealed class DetectTimeLimitOpenApiTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<JsonElement> ReadDetectOperationAsync() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    return document.RootElement.GetProperty("paths").GetProperty("/api/images/detect").GetProperty("post").Clone();
  }

  [Fact]
  public async Task DetectHasDetectionTimeoutExample() {
    var operation = await ReadDetectOperationAsync().ConfigureAwait(false);

    var example = operation.GetProperty("responses").GetProperty("504")
      .GetProperty("content").GetProperty("application/json").GetProperty("example");
    example.GetProperty("code").GetString().Should().Be("detection_timeout");
    example.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task DetectDescriptionNamesTimeLimitAndLimitsHit() {
    var operation = await ReadDetectOperationAsync().ConfigureAwait(false);

    operation.GetProperty("description").GetString().Should()
      .Contain("detection_timeout").And.Contain("limitsHit").And.Contain("TimeoutMs");
  }
}
