using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

#pragma warning disable CA2007, CA2000

namespace GameBot.ContractTests.Queues;

/// <summary>
/// Feature 184: the contract of POST /api/queues/{id}/run-next (<c>specs/184-showdown-run-next/contracts/run-next-api.md</c>).
/// The 404, 400, and not_running cases use the real service. The other outcomes use a fake execution service, because a real
/// run needs a device. The end-to-end case is in the integration project.
/// </summary>
public sealed class QueueRunNextApiContractTests : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;

  public QueueRunNextApiContractTests() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");

    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    GC.SuppressFinalize(this);
  }

  private sealed class FakeExecution : IQueueExecutionService {
    private readonly RunNextResult _result;
    public FakeExecution(RunNextResult result) { _result = result; }
    public Task<QueueStartOutcome> StartAsync(string queueId, CancellationToken ct = default) => Task.FromResult(QueueStartOutcome.NotFound);
    public Task StopAsync(string queueId, CancellationToken ct = default) => Task.CompletedTask;
    public bool IsRunning(string queueId) => false;
    public LiveScheduleResult ScheduleRelative(string queueId, string sequenceId, TimeSpan offset) => new(LiveScheduleOutcome.NotRunning, default);
    public RunNextResult RunNext(string queueId, string entryId) => _result;
  }

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static WebApplicationFactory<Program> WithFake(WebApplicationFactory<Program> app, RunNextResult result) =>
    app.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
      services.RemoveAll<IQueueExecutionService>();
      services.AddSingleton<IQueueExecutionService>(new FakeExecution(result));
    }));

  private static async Task<string> CreateQueueAsync(HttpClient client) {
    var resp = await client.PostAsJsonAsync(new Uri("/api/queues", UriKind.Relative),
      new { name = "Farm", emulatorSerial = "emu-offline", cycleExecution = false }).ConfigureAwait(true);
    return JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement.GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> ReadAsync(HttpResponseMessage resp) =>
    JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(true)).RootElement;

  [Fact]
  public async Task UnknownQueueReturns404() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);

    var resp = await client.PostAsJsonAsync(new Uri("/api/queues/missing/run-next", UriKind.Relative), new { entryId = "e1" }).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Theory]
  [InlineData("{}")]
  [InlineData("{\"entryId\":\"\"}")]
  [InlineData("{\"entryId\":\"   \"}")]
  public async Task AMissingOrEmptyEntryIdReturns400(string body) {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var queueId = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsync(new Uri($"/api/queues/{queueId}/run-next", UriKind.Relative),
      new StringContent(body, Encoding.UTF8, "application/json")).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var json = await ReadAsync(resp).ConfigureAwait(true);
    json.GetProperty("error").GetProperty("code").GetString().Should().Be("invalid_request");
    json.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task AQueueWithNoActiveRunReturns409NotRunning() {
    using var app = new WebApplicationFactory<Program>();
    var client = NewClient(app);
    var queueId = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/run-next", UriKind.Relative), new { entryId = "e1" }).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var json = await ReadAsync(resp).ConfigureAwait(true);
    json.GetProperty("error").GetProperty("code").GetString().Should().Be("not_running");
    json.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
  }

  [Theory]
  [InlineData("Queued", false)]
  [InlineData("AlreadyQueued", true)]
  public async Task AnIdleOrQueuedEntryReturns200(string outcomeName, bool alreadyQueued) {
    using var root = new WebApplicationFactory<Program>();
    using var app = WithFake(root, new RunNextResult(Enum.Parse<RunNextOutcome>(outcomeName), "seq-1"));
    var client = NewClient(app);
    var queueId = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/run-next", UriKind.Relative), new { entryId = "e1" }).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var json = await ReadAsync(resp).ConfigureAwait(true);
    json.GetProperty("entryId").GetString().Should().Be("e1");
    json.GetProperty("sequenceId").GetString().Should().Be("seq-1");
    json.GetProperty("alreadyQueued").GetBoolean().Should().Be(alreadyQueued);
    json.GetProperty("state").GetString().Should().Be("queued");
  }

  [Theory]
  [InlineData("NotRunning", "not_running")]
  [InlineData("EntryNotAvailable", "entry_not_available")]
  [InlineData("EntryRunning", "entry_running")]
  [InlineData("EntryScheduled", "entry_scheduled")]
  public async Task ARefusedEntryReturns409WithItsCode(string outcomeName, string code) {
    using var root = new WebApplicationFactory<Program>();
    using var app = WithFake(root, new RunNextResult(Enum.Parse<RunNextOutcome>(outcomeName)));
    var client = NewClient(app);
    var queueId = await CreateQueueAsync(client).ConfigureAwait(true);

    var resp = await client.PostAsJsonAsync(new Uri($"/api/queues/{queueId}/run-next", UriKind.Relative), new { entryId = "e1" }).ConfigureAwait(true);

    resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var json = await ReadAsync(resp).ConfigureAwait(true);
    json.GetProperty("error").GetProperty("code").GetString().Should().Be(code);
    json.GetProperty("error").GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
  }
}
