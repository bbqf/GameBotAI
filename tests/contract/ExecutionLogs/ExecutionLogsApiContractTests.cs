using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.ContractTests.ExecutionLogs;

public sealed class ExecutionLogsApiContractTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynPort;
  private readonly string? _prevToken;

  public ExecutionLogsApiContractTests() {
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
  public async Task ListEndpointAcceptsSortAndFilterParameters() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var service = app.Services.GetRequiredService<GameBot.Service.Services.ExecutionLog.IExecutionLogService>();
    await service.LogCommandExecutionAsync(
      "cmd-contract-list",
      "Contract Command",
      "success",
      Array.Empty<GameBot.Service.Services.PrimitiveTapStepOutcome>(),
      new GameBot.Service.Services.ExecutionLog.ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

    var response = await client.GetAsync(new Uri("/api/execution-logs?sortBy=timestamp&sortDirection=desc&filterObjectName=contract&pageSize=50", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var root = doc.RootElement;
    root.TryGetProperty("items", out var items).Should().BeTrue();
    items.ValueKind.Should().Be(JsonValueKind.Array);
    root.TryGetProperty("nextPageToken", out _).Should().BeTrue();
  }

  [Fact]
  public async Task DetailEndpointReturnsRequiredDetailFields() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var service = app.Services.GetRequiredService<GameBot.Service.Services.ExecutionLog.IExecutionLogService>();
    await service.LogCommandExecutionAsync(
      "cmd-contract-detail",
      "Contract Detail Command",
      "failure",
      Array.Empty<GameBot.Service.Services.PrimitiveTapStepOutcome>(),
      new GameBot.Service.Services.ExecutionLog.ExecutionLogContext { Depth = 0 }).ConfigureAwait(false);

    var list = await client.GetAsync(new Uri("/api/execution-logs?filterObjectName=contract%20detail&pageSize=1", UriKind.Relative)).ConfigureAwait(false);
    list.EnsureSuccessStatusCode();

    using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync().ConfigureAwait(false));
    var id = listDoc.RootElement.GetProperty("items")[0].GetProperty("id").GetString();

    var detail = await client.GetAsync(new Uri($"/api/execution-logs/{id}", UriKind.Relative)).ConfigureAwait(false);
    detail.EnsureSuccessStatusCode();

    using var detailDoc = JsonDocument.Parse(await detail.Content.ReadAsStringAsync().ConfigureAwait(false));
    var root = detailDoc.RootElement;
    root.TryGetProperty("executionId", out _).Should().BeTrue();
    root.TryGetProperty("summary", out _).Should().BeTrue();
    root.TryGetProperty("relatedObjects", out _).Should().BeTrue();
    root.TryGetProperty("snapshot", out _).Should().BeTrue();
    root.TryGetProperty("stepOutcomes", out _).Should().BeTrue();
  }

  [Fact]
  public async Task DetailEndpointReturnsNotFoundForUnknownExecutionId() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var response = await client.GetAsync(new Uri("/api/execution-logs/does-not-exist", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
  }
}
