using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.Queues;

/// <summary>Feature 189: the smart scheduling members of the queue routes (contracts/rest-api.md).</summary>
public sealed class SmartSchedulingQueueContractTests {
  private const string WindowError = "smartSchedulingWindowMinutes must be from 1 to 120";

  private static Uri Rel(string path) => new(path, UriKind.Relative);

  private static (WebApplicationFactory<Program> App, HttpClient Client) NewHost() {
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return (app, client);
  }

  private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) {
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.Clone();
  }

  private static async Task<string> CreateAsync(HttpClient client, object body) {
    var response = await client.PostAsJsonAsync(Rel("/api/queues"), body);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    return (await ReadAsync(response)).GetProperty("id").GetString()!;
  }

  private static async Task<JsonElement> GetAsync(HttpClient client, string id) =>
    await ReadAsync(await client.GetAsync(Rel($"/api/queues/{id}")));

  private static void ShouldHave(JsonElement queue, bool smart, int window) {
    queue.GetProperty("smartScheduling").GetBoolean().Should().Be(smart);
    queue.GetProperty("smartSchedulingWindowMinutes").GetInt32().Should().Be(window);
  }

  [Fact]
  public async Task ACreateWithNoMemberGivesOffAnd15() {
    var (app, client) = NewHost();
    using var _ = app;

    var id = await CreateAsync(client, new { name = "Q", emulatorSerial = "e" });

    ShouldHave(await GetAsync(client, id), false, 15);
    var list = await ReadAsync(await client.GetAsync(Rel("/api/queues")));
    ShouldHave(list.EnumerateArray().Single(q => q.GetProperty("id").GetString() == id), false, 15);
  }

  [Fact]
  public async Task ACreateWithTheMembersKeepsThem() {
    var (app, client) = NewHost();
    using var _ = app;

    var response = await client.PostAsJsonAsync(Rel("/api/queues"),
      new { name = "Q", emulatorSerial = "e", smartScheduling = true, smartSchedulingWindowMinutes = 20 });

    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var created = await ReadAsync(response);
    ShouldHave(created, true, 20);
    ShouldHave(await GetAsync(client, created.GetProperty("id").GetString()!), true, 20);
  }

  [Fact]
  public async Task AnUpdateWithNoMemberKeepsTheStoredValues() {
    var (app, client) = NewHost();
    using var _ = app;
    var id = await CreateAsync(client, new { name = "Q", emulatorSerial = "e", smartScheduling = true, smartSchedulingWindowMinutes = 30 });

    var response = await client.PutAsJsonAsync(Rel($"/api/queues/{id}"), new { name = "Q2" });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    ShouldHave(await ReadAsync(response), true, 30);
    ShouldHave(await GetAsync(client, id), true, 30);
  }

  [Fact]
  public async Task AnUpdateThatTurnsTheSwitchOffKeepsTheWindow() {
    var (app, client) = NewHost();
    using var _ = app;
    var id = await CreateAsync(client, new { name = "Q", emulatorSerial = "e", smartScheduling = true, smartSchedulingWindowMinutes = 30 });

    var response = await client.PutAsJsonAsync(Rel($"/api/queues/{id}"), new { name = "Q", smartScheduling = false });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    ShouldHave(await GetAsync(client, id), false, 30);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(121)]
  [InlineData(-1)]
  public async Task AWindowOutOfRangeGives400OnCreateAndOnUpdate(int window) {
    var (app, client) = NewHost();
    using var _ = app;

    var create = await client.PostAsJsonAsync(Rel("/api/queues"),
      new { name = "Q", emulatorSerial = "e", smartScheduling = false, smartSchedulingWindowMinutes = window });
    create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var createError = (await ReadAsync(create)).GetProperty("error");
    createError.GetProperty("code").GetString().Should().Be("invalid_request");
    createError.GetProperty("message").GetString().Should().Be(WindowError);

    var id = await CreateAsync(client, new { name = "Q", emulatorSerial = "e" });
    var update = await client.PutAsJsonAsync(Rel($"/api/queues/{id}"), new { name = "Q", smartSchedulingWindowMinutes = window });
    update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    (await ReadAsync(update)).GetProperty("error").GetProperty("message").GetString().Should().Be(WindowError);
    ShouldHave(await GetAsync(client, id), false, 15);
  }

  [Theory]
  [InlineData(1)]
  [InlineData(120)]
  public async Task TheEndsOfTheRangeAreAccepted(int window) {
    var (app, client) = NewHost();
    using var _ = app;

    var id = await CreateAsync(client, new { name = "Q", emulatorSerial = "e", smartScheduling = true, smartSchedulingWindowMinutes = window });
    var update = await client.PutAsJsonAsync(Rel($"/api/queues/{id}"), new { name = "Q", smartSchedulingWindowMinutes = window });

    update.StatusCode.Should().Be(HttpStatusCode.OK);
    ShouldHave(await GetAsync(client, id), true, window);
  }

  [Fact]
  public async Task ADuplicateCopiesTheTwoValues() {
    var (app, client) = NewHost();
    using var _ = app;
    var id = await CreateAsync(client, new { name = "Q", emulatorSerial = "e", smartScheduling = true, smartSchedulingWindowMinutes = 25 });

    var response = await client.PostAsJsonAsync(Rel($"/api/queues/{id}/duplicate"), new { name = "Q copy", emulatorSerial = "e" });

    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var copy = await ReadAsync(response);
    ShouldHave(copy, true, 25);
    ShouldHave(await GetAsync(client, copy.GetProperty("id").GetString()!), true, 25);
  }

  [Fact]
  public async Task TheMonitorResponseHasTheSmartSchedulingBlock() {
    var (app, client) = NewHost();
    using var _ = app;
    var id = await CreateAsync(client, new { name = "Q", emulatorSerial = "e", smartScheduling = true, smartSchedulingWindowMinutes = 20 });

    var monitor = await ReadAsync(await client.GetAsync(Rel($"/api/queues/{id}/monitor")));

    var block = monitor.GetProperty("smartScheduling");
    block.GetProperty("enabled").GetBoolean().Should().BeTrue();
    block.GetProperty("windowMinutes").GetInt32().Should().Be(20);
  }

  private static async Task<JsonElement> ReadDocumentAsync() {
    var (app, client) = NewHost();
    using var _ = app;
    return await ReadAsync(await client.GetAsync(Rel("/swagger/v1/swagger.json")));
  }

  private static string? Description(JsonElement document, string schema, string property) {
    var element = document.GetProperty("components").GetProperty("schemas").GetProperty(schema)
      .GetProperty("properties").GetProperty(property);
    return element.TryGetProperty("description", out var description) ? description.GetString() : null;
  }

  [Theory]
  [InlineData("CreateQueueRequest")]
  [InlineData("UpdateQueueRequest")]
  [InlineData("QueueResponse")]
  [InlineData("QueueDetailResponse")]
  public async Task TheOpenApiDocumentDescribesTheTwoMembers(string schema) {
    var document = await ReadDocumentAsync();

    Description(document, schema, "smartScheduling").Should().Contain("pending firing").And.Contain("Default false");
    Description(document, schema, "smartSchedulingWindowMinutes").Should().Contain("from 1 to 120").And.Contain("Default 15");
  }

  [Fact]
  public async Task TheOpenApiDocumentDescribesTheMonitorMembers() {
    var document = await ReadDocumentAsync();

    document.GetProperty("components").GetProperty("schemas").GetProperty("QueueMonitorSmartSchedulingResponse")
      .GetProperty("description").GetString().Should().Contain("stored queue");
    Description(document, "QueueMonitorSmartSchedulingResponse", "windowMinutes").Should().Contain("from 1 to 120");
    Description(document, "QueueMonitorItemResponse", "requestedAt").Should().Contain("requested time");
  }
}
