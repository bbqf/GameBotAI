using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 175: a full run of a WaitForImage step with <c>until</c> <c>absent</c>. The fake screen is one
/// white pixel. A white reference image is on the screen. A black reference image is not on the screen.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class WaitForImageAbsentSequenceIntegrationTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;
  private readonly string? _prevCaptureInterval;

  private const string WhitePngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public WaitForImageAbsentSequenceIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevCaptureInterval = Environment.GetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", WhitePngBase64);
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", "10");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_CAPTURE_INTERVAL_MS", _prevCaptureInterval);
    Environment.SetEnvironmentVariable("GAMEBOT_TEST_SCREEN_IMAGE_B64", null);
    GC.SuppressFinalize(this);
  }

#pragma warning disable CA1416 // Windows-only System.Drawing, as the image evaluator is.
  private static string BlackPngBase64() {
    using var bmp = new Bitmap(1, 1);
    bmp.SetPixel(0, 0, Color.Black);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return Convert.ToBase64String(ms.ToArray());
  }
#pragma warning restore CA1416

  [Fact]
  public async Task ImageNotOnTheScreenEndsWithImageAbsent() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorized(app);
    (await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "gone-image", data = BlackPngBase64() }).ConfigureAwait(false))
      .StatusCode.Should().Be(HttpStatusCode.Created);

    var steps = await RunAsync(app, client, new {
      timeoutMs = 2000,
      until = "absent",
      absentCaptures = 2,
      detectionTarget = new { referenceImageId = "gone-image", confidence = 0.99 }
    }).ConfigureAwait(false);

    steps[0].GetProperty("actionOutcome").GetString().Should().Be("image_absent");
  }

  [Fact]
  public async Task ImageOnTheScreenEndsWithTimeoutElapsed() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorized(app);
    (await client.PostAsJsonAsync(new Uri("/api/images", UriKind.Relative), new { id = "stay-image", data = WhitePngBase64 }).ConfigureAwait(false))
      .StatusCode.Should().Be(HttpStatusCode.Created);

    var steps = await RunAsync(app, client, new {
      timeoutMs = 120,
      until = "absent",
      detectionTarget = new { referenceImageId = "stay-image", confidence = 0.99 }
    }).ConfigureAwait(false);

    steps[0].GetProperty("actionOutcome").GetString().Should().Be("timeout_elapsed");
  }

  [Fact]
  public async Task MissingImageEndsWithImageUnavailable() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorized(app);

    var steps = await RunAsync(app, client, new {
      timeoutMs = 40,
      until = "absent",
      detectionTarget = new { referenceImageId = "no-such-image", confidence = 0.99 }
    }).ConfigureAwait(false);

    steps[0].GetProperty("actionOutcome").GetString().Should().Be("image_unavailable");
  }

  [Fact]
  public async Task InvalidUntilIsRejectedOnSave() {
    using var app = new WebApplicationFactory<Program>();
    var client = Authorized(app);

    var response = await client.PostAsJsonAsync("/api/sequences", BuildRequest(new {
      timeoutMs = 40,
      until = "gone",
      detectionTarget = new { referenceImageId = "x", confidence = 0.9 }
    })).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Should().Contain("until must be 'present' or 'absent'");
  }

  private static HttpClient Authorized(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    return client;
  }

  private static object BuildRequest(object waitPayload) => new {
    name = "wait-absent-sequence",
    version = 1,
    steps = new object[] {
      new {
        stepId = "wait-step",
        primitiveAction = new { type = "WaitForImage", schemaVersion = "v1", payload = waitPayload }
      },
      new {
        stepId = "after-wait",
        primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId = "cmd-after-wait" } }
      }
    }
  };

  private static async Task<JsonElement> RunAsync(WebApplicationFactory<Program> app, HttpClient client, object waitPayload) {
    var commands = app.Services.GetRequiredService<ICommandRepository>();
    await commands.AddAsync(new Command { Id = "cmd-after-wait", Name = "After wait" }).ConfigureAwait(false);
    var createResponse = await client.PostAsJsonAsync("/api/sequences", BuildRequest(waitPayload)).ConfigureAwait(false);
    createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    var sequenceId = created.GetProperty("id").GetString()!;
    await commands.DeleteAsync("cmd-after-wait").ConfigureAwait(false);

    var executeResponse = await client.PostAsJsonAsync($"/api/sequences/{sequenceId}/execute", new { }).ConfigureAwait(false);
    executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    var execution = await executeResponse.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);
    return execution.GetProperty("steps").Clone();
  }
}
