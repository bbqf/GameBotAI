using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sequences;

public sealed class SequenceConditionalFlowOpenApiTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynPort;
  private readonly string? _prevToken;

  public SequenceConditionalFlowOpenApiTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task SwaggerDocumentIncludesSequenceConditionalFlowEndpoints() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var paths = document.RootElement.GetProperty("paths");

    paths.TryGetProperty("/api/sequences", out var sequences).Should().BeTrue();
    sequences.TryGetProperty("post", out _).Should().BeTrue();

    paths.TryGetProperty("/api/sequences/{sequenceId}", out var sequenceById).Should().BeTrue();
    sequenceById.TryGetProperty("get", out _).Should().BeTrue();
    sequenceById.TryGetProperty("patch", out _).Should().BeTrue();

    paths.TryGetProperty("/api/sequences/{sequenceId}/validate", out var validate).Should().BeTrue();
    validate.TryGetProperty("post", out _).Should().BeTrue();

    paths.TryGetProperty("/api/sequences/{sequenceId}/execute", out var execute).Should().BeTrue();
    execute.TryGetProperty("post", out _).Should().BeTrue();
  }

  [Fact]
  public async Task SwaggerDocumentIncludesSequenceConditionalFlowSchemas() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var response = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    json.Should().Contain("SequenceFlow");
    json.Should().Contain("SequenceFlowUpsertRequest");
    json.Should().Contain("ConditionExpression");
    json.Should().Contain("ConditionOperand");
    json.Should().Contain("SequenceSaveConflict");
    json.Should().Contain("ConditionEvaluationTrace");
    json.Should().Contain("AuthoringDeepLink");
  }
}
