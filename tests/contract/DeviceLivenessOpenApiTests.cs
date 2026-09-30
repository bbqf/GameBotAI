#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

/// <summary>
/// Feature 106 (FR-020): the OpenAPI document describes the liveness block, the capture headers, the
/// new errors and the queue device liveness. Swagger reads no XML comments, so these descriptions come
/// from the schema filter and the operation filter.
/// </summary>
public sealed class DeviceLivenessOpenApiTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<JsonElement> ReadDocumentAsync() {
    using var app = CreateFactory();
    var client = app.CreateClient();
    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.EnsureSuccessStatusCode();
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.Clone();
  }

  private static JsonElement Schema(JsonElement document, string name) =>
    document.GetProperty("components").GetProperty("schemas").GetProperty(name);

  private static string[] EnumOf(JsonElement property) =>
    property.GetProperty("enum").EnumerateArray().Select(v => v.GetString()!).ToArray();

  // ── US1: session health ────────────────────────────────────────────────

  [Fact]
  public async Task SessionHealthSchemaHasTheLivenessBlock() {
    var document = await ReadDocumentAsync();

    Schema(document, "SessionHealthSchema").GetProperty("properties").TryGetProperty("liveness", out _).Should().BeTrue();
    var props = Schema(document, "SessionLivenessSchema").GetProperty("properties");
    foreach (var field in new[] { "state", "reason", "frameAgeMs", "unchangedMs", "stale", "lastInputAt", "lastInputOutcome" }) {
      props.TryGetProperty(field, out var p).Should().BeTrue(field);
      p.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(field);
    }
  }

  [Fact]
  public async Task SessionLivenessEnumsListTheContractValues() {
    var document = await ReadDocumentAsync();
    var props = Schema(document, "SessionLivenessSchema").GetProperty("properties");

    EnumOf(props.GetProperty("state")).Should().Equal("live", "not_live", "unknown");
    EnumOf(props.GetProperty("reason")).Should().BeEquivalentTo("capture_stalled", "input_timeout", "no_change_after_input", "transport_not_ready");
    EnumOf(props.GetProperty("lastInputOutcome")).Should().Equal("pending", "completed", "timed_out", "failed", "cancelled");
  }

  // ── US2: capture headers and 504 ───────────────────────────────────────

  [Theory]
  [InlineData("/api/emulator/screenshot")]
  [InlineData("/api/sessions/{id}/snapshot")]
  public async Task CaptureOperationsDocumentTheHeadersAndThe504(string path) {
    var document = await ReadDocumentAsync();
    var operation = document.GetProperty("paths").GetProperty(path).GetProperty("get");
    var responses = operation.GetProperty("responses");

    var headers = responses.GetProperty("200").GetProperty("headers");
    foreach (var name in new[] { "X-Capture-Age-Ms", "X-Capture-Unchanged-Ms", "X-Capture-Stale" }) {
      headers.TryGetProperty(name, out var header).Should().BeTrue(name);
      header.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(name);
    }
    responses.TryGetProperty("504", out var timeout).Should().BeTrue();
    timeout.GetProperty("description").GetString().Should().Contain("capture_timeout");
  }

  // ── US3: inputs 503 and 504 ────────────────────────────────────────────

  [Fact]
  public async Task InputsOperationDocumentsThe503AndThe504WithExamples() {
    var document = await ReadDocumentAsync();
    var responses = document.GetProperty("paths").GetProperty("/api/sessions/{id}/inputs")
      .GetProperty("post").GetProperty("responses");

    var notLive = responses.GetProperty("503");
    notLive.GetProperty("description").GetString().Should().Contain("device_not_live");
    notLive.GetProperty("content").GetProperty("application/json").GetProperty("example")
      .GetProperty("error").GetProperty("code").GetString().Should().Be("device_not_live");

    var timeout = responses.GetProperty("504");
    timeout.GetProperty("description").GetString().Should().Contain("device_timeout");
    var example = timeout.GetProperty("content").GetProperty("application/json").GetProperty("example");
    example.GetProperty("error").GetProperty("code").GetString().Should().Be("device_timeout");
    example.GetProperty("results").GetArrayLength().Should().Be(2);
  }

  // ── US4: queue device liveness ─────────────────────────────────────────

  [Fact]
  public async Task QueueHealthHasTheDeviceLivenessBlock() {
    var document = await ReadDocumentAsync();

    var health = Schema(document, "QueueHealthResponse").GetProperty("properties");
    health.TryGetProperty("deviceLiveness", out _).Should().BeTrue();

    var schema = Schema(document, "QueueDeviceLivenessResponse");
    schema.GetProperty("description").GetString().Should().Contain("holds");
    var props = schema.GetProperty("properties");
    foreach (var field in new[] { "state", "reason", "notLiveSince", "stale", "frameAgeMs", "unchangedMs", "gatedFirings" }) {
      props.TryGetProperty(field, out var p).Should().BeTrue(field);
      p.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(field);
    }
    EnumOf(props.GetProperty("state")).Should().Equal("live", "not_live", "unknown");
    EnumOf(props.GetProperty("reason")).Should().BeEquivalentTo("capture_stalled", "input_timeout", "no_change_after_input", "transport_not_ready");
  }

  // ── Feature 121: alert, recovery state and the deviceRecovery field ────

  [Fact]
  public async Task QueueDeviceLivenessDescribesTheAlertAndTheRecoveryMembers() {
    var document = await ReadDocumentAsync();
    var schema = Schema(document, "QueueDeviceLivenessResponse");

    var description = schema.GetProperty("description").GetString()!;
    description.Should().Contain("reboot the instance when deviceRecovery is on");
    description.Should().NotContain("never stops or recovers the device", "the old statement is replaced (FR-016)");
    var props = schema.GetProperty("properties");
    foreach (var field in new[] { "alertSent", "recoveryAttempts", "recoveryState" }) {
      props.TryGetProperty(field, out var p).Should().BeTrue(field);
      p.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(field);
    }
    EnumOf(props.GetProperty("recoveryState")).Should().Equal("idle", "running", "exhausted");
  }

  [Fact]
  public async Task TheDeviceRecoverySchemaDescribesEachMember() {
    var document = await ReadDocumentAsync();
    var schema = Schema(document, "QueueDeviceRecoveryDto");

    schema.GetProperty("description").GetString().Should().Contain("reboots its LDPlayer instance");
    var props = schema.GetProperty("properties");
    foreach (var field in new[] { "action", "afterMs", "maxAttempts", "cooldownMs" }) {
      props.TryGetProperty(field, out var p).Should().BeTrue(field);
      p.GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(field);
    }
    EnumOf(props.GetProperty("action")).Should().Equal("none", "reboot-instance");
  }

  [Theory]
  [InlineData("CreateQueueRequest")]
  [InlineData("UpdateQueueRequest")]
  [InlineData("QueueResponse")]
  [InlineData("QueueDetailResponse")]
  public async Task QueueSchemasHaveTheDeviceRecoveryField(string schemaName) {
    var document = await ReadDocumentAsync();

    Schema(document, schemaName).GetProperty("properties").TryGetProperty("deviceRecovery", out _).Should().BeTrue();
  }
}
