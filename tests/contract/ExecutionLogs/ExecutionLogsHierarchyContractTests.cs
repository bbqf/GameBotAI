using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.ContractTests.ExecutionLogs;

public sealed class ExecutionLogsHierarchyContractTests : IDisposable {
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynPort;
  private readonly string? _prevToken;

  public ExecutionLogsHierarchyContractTests() {
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
  public async Task ListReturnsRootsOnlyWithStatusAndChildCount() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var token = Token();
    var commandName = $"{token}-CommandA";
    var svc = app.Services.GetRequiredService<IExecutionLogService>();
    await SeedSequenceRunAsync(svc, $"{token} Alpha", token, commandName).ConfigureAwait(false);

    var response = await client.GetAsync(new Uri($"/api/execution-logs?filterObjectName={token}&pageSize=50", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var items = doc.RootElement.GetProperty("items");
    items.GetArrayLength().Should().Be(1, "the invoked commands must not appear as their own list rows");

    var item = items[0];
    var hierarchy = item.GetProperty("hierarchy");
    (hierarchy.TryGetProperty("parentExecutionId", out var parent) && parent.ValueKind == JsonValueKind.String)
      .Should().BeFalse("the list must contain only top-level entries");
    item.GetProperty("finalStatus").GetString().Should().Be("success");
    item.GetProperty("childCount").ValueKind.Should().Be(JsonValueKind.Number);
    item.GetProperty("objectRef").GetProperty("displayNameSnapshot").GetString().Should().Be($"{token} Alpha");
    // The invoked command must not appear as its own list row (it may appear inside the
    // sequence's own step details, which is expected).
    items.EnumerateArray()
      .Select(i => i.GetProperty("objectRef").GetProperty("displayNameSnapshot").GetString())
      .Should().NotContain(commandName);
  }

  [Fact]
  public async Task ListSortAndFilterApplyOverRootsOnly() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var token = Token();
    var commandName = $"{token}-CommandA";
    var svc = app.Services.GetRequiredService<IExecutionLogService>();
    await SeedSequenceRunAsync(svc, $"{token} Alpha", token + "-a", commandName).ConfigureAwait(false);
    await SeedSequenceRunAsync(svc, $"{token} Zulu", token + "-z", commandName).ConfigureAwait(false);

    var sorted = await client.GetAsync(new Uri($"/api/execution-logs?sortBy=objectName&sortDirection=asc&filterObjectName={token}&pageSize=50", UriKind.Relative)).ConfigureAwait(false);
    sorted.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await sorted.Content.ReadAsStringAsync().ConfigureAwait(false));
    var names = doc.RootElement.GetProperty("items").EnumerateArray()
      .Select(i => i.GetProperty("objectRef").GetProperty("displayNameSnapshot").GetString())
      .ToList();

    names.Should().HaveCount(2, "only the two sequence roots match — no child command rows leak in");
    names[0].Should().Be($"{token} Alpha");
    names[1].Should().Be($"{token} Zulu");
    names.Should().NotContain(commandName);
  }

  [Fact]
  public async Task SubtreeEndpointReturnsNestedTree() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var token = Token();
    var svc = app.Services.GetRequiredService<IExecutionLogService>();
    var rootId = await SeedSequenceRunAsync(svc, $"{token} Alpha", token, $"{token}-CommandA").ConfigureAwait(false);

    var response = await client.GetAsync(new Uri($"/api/execution-logs/{rootId}/subtree", UriKind.Relative)).ConfigureAwait(false);
    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    var root = doc.RootElement;
    root.GetProperty("executionId").GetString().Should().Be(rootId);
    root.TryGetProperty("finalStatus", out _).Should().BeTrue();
    root.GetProperty("root").GetProperty("children").GetArrayLength().Should().Be(2);
  }

  [Fact]
  public async Task SubtreeEndpointReturnsNotFoundForUnknownId() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

    var response = await client.GetAsync(new Uri("/api/execution-logs/does-not-exist/subtree", UriKind.Relative)).ConfigureAwait(false);
    response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
  }

  private static string Token() => "ctrhier" + Guid.NewGuid().ToString("N")[..8];

  private static async Task<string> SeedSequenceRunAsync(IExecutionLogService svc, string sequenceName, string sequenceId, string commandName) {
    var rootId = await svc.LogSequenceStartAsync(sequenceId, sequenceName).ConfigureAwait(false);
    await svc.LogCommandExecutionAsync("cmd-a", commandName, "success", Array.Empty<PrimitiveTapStepOutcome>(),
      new ExecutionLogContext { ParentExecutionId = rootId, RootExecutionId = rootId, Depth = 1, SequenceIndex = 1, SequenceId = sequenceId, SequenceLabel = sequenceName }).ConfigureAwait(false);
    await svc.LogCommandExecutionAsync("cmd-b", commandName + "B", "success", Array.Empty<PrimitiveTapStepOutcome>(),
      new ExecutionLogContext { ParentExecutionId = rootId, RootExecutionId = rootId, Depth = 1, SequenceIndex = 2, SequenceId = sequenceId, SequenceLabel = sequenceName }).ConfigureAwait(false);

    var details = new List<ExecutionDetailItem> {
      Step(1, "cmd-a", commandName, sequenceId, sequenceName),
      Step(2, "cmd-b", commandName + "B", sequenceId, sequenceName)
    };
    await svc.LogSequenceFinalizeAsync(rootId, sequenceId, sequenceName, "success",
      $"Sequence '{sequenceName}' success with 2 steps executed.",
      new ExecutionLogContext { Depth = 0, SequenceId = sequenceId, SequenceLabel = sequenceName }, details).ConfigureAwait(false);
    return rootId;
  }

  private static ExecutionDetailItem Step(int order, string commandId, string commandName, string seqId, string seqLabel)
    => new("step", $"Step ran command '{commandName}'.", new Dictionary<string, object?> {
        ["stepOrder"] = order, ["stepType"] = "command", ["status"] = "executed", ["actionOutcome"] = "executed",
        ["commandId"] = commandId, ["commandName"] = commandName, ["sequenceId"] = seqId, ["sequenceLabel"] = seqLabel,
        ["stepId"] = $"step-{order}", ["stepLabel"] = $"Step {order}"
      }, "normal");
}
