#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Sessions;

/// <summary>Feature 141 (US2): <c>POST</c> and <c>GET /api/sessions/{id}/restart</c>.</summary>
public sealed class SessionMonitoringRestartContractTests : MonitoringContractTestBase {
  private static Uri RestartUri(string id) => new($"/api/sessions/{id}/restart", UriKind.Relative);

  private static async Task<JsonElement> WaitForEndAsync(HttpClient client, string id) {
    for (var i = 0; i < 200; i++) {
      var resp = await client.GetAsync(RestartUri(id));
      resp.StatusCode.Should().Be(HttpStatusCode.OK);
      var record = await ReadJsonAsync(resp);
      if (record.GetProperty("state").GetString() != "restarting") return record;
      await Task.Delay(25);
    }

    throw new TimeoutException("The restart did not end.");
  }

  private static string ErrorCode(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString()!;

  [Fact]
  public async Task UnknownSessionGives404ForPostAndGet() {
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, _ => { });
    var client = Client(app);

    var post = await client.PostAsync(RestartUri("does-not-exist"), null);
    var get = await client.GetAsync(RestartUri("does-not-exist"));

    post.StatusCode.Should().Be(HttpStatusCode.NotFound);
    ErrorCode(await ReadJsonAsync(post)).Should().Be("not_found");
    get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    ErrorCode(await ReadJsonAsync(get)).Should().Be("not_found");
  }

  [Fact]
  public async Task RestartIsAcceptedAndEndsAsSuccessAndReconnectsTheSession() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emulator-5558");
    var control = new FakeEmulatorControl();
    var reconnector = new FakeReconnectorDouble();
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => UseRestartFakes(services, sessions, control, reconnector));
    var client = Client(app);

    var post = await client.PostAsync(RestartUri(session.Id), null);

    post.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var accepted = await ReadJsonAsync(post);
    accepted.GetProperty("sessionId").GetString().Should().Be(session.Id);
    accepted.GetProperty("startedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
    var record = await WaitForEndAsync(client, session.Id);
    record.GetProperty("state").GetString().Should().Be("success");
    record.GetProperty("step").GetString().Should().Be("done");
    record.GetProperty("reason").ValueKind.Should().Be(JsonValueKind.Null);
    record.GetProperty("endedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
    control.RebootedIndexes.Should().ContainSingle().Which.Should().Be(2);
    reconnector.Calls.Should().ContainSingle().Which.Should().Be((session.Id, "emulator-5558"));
  }

  [Fact]
  public async Task SecondPostWhileRestartingGives409AndTheRecordStays() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emulator-5558");
    var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    var control = new FakeEmulatorControl { RebootGate = gate };
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => UseRestartFakes(services, sessions, control, new FakeReconnectorDouble()));
    var client = Client(app);

    var first = await client.PostAsync(RestartUri(session.Id), null);
    var second = await client.PostAsync(RestartUri(session.Id), null);

    first.StatusCode.Should().Be(HttpStatusCode.Accepted);
    var firstStarted = (await ReadJsonAsync(first)).GetProperty("startedAtUtc").GetString();
    second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    ErrorCode(await ReadJsonAsync(second)).Should().Be("restart_in_progress");
    var running = await ReadJsonAsync(await client.GetAsync(RestartUri(session.Id)));
    running.GetProperty("state").GetString().Should().Be("restarting");
    running.GetProperty("startedAtUtc").GetString().Should().Be(firstStarted);

    gate.SetResult(true);
    (await WaitForEndAsync(client, session.Id)).GetProperty("state").GetString().Should().Be("success");
    control.RebootedIndexes.Should().HaveCount(1);
  }

  [Fact]
  public async Task SessionWithoutASerialGives422AndTheEmulatorIsNotChanged() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add(null);
    var control = new FakeEmulatorControl();
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => UseRestartFakes(services, sessions, control, new FakeReconnectorDouble()));
    var client = Client(app);

    var post = await client.PostAsync(RestartUri(session.Id), null);

    post.StatusCode.Should().Be((HttpStatusCode)422);
    var body = await ReadJsonAsync(post);
    ErrorCode(body).Should().Be("restart_unavailable");
    body.GetProperty("reason").GetString().Should().Be("no_device_serial");
    control.RebootedIndexes.Should().BeEmpty();
    (await client.GetAsync(RestartUri(session.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
  }

  [Fact]
  public async Task GetBeforeAnyRestartGives404NoRestart() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emulator-5558");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => UseRestartFakes(services, sessions, new FakeEmulatorControl(), new FakeReconnectorDouble()));

    var get = await Client(app).GetAsync(RestartUri(session.Id));

    get.StatusCode.Should().Be(HttpStatusCode.NotFound);
    ErrorCode(await ReadJsonAsync(get)).Should().Be("no_restart");
  }

  [Fact]
  public async Task RestartOfOneSessionDoesNotChangeAnotherSession() {
    var sessions = new LivenessFakeSessionManager();
    var first = sessions.Add("emulator-5558");
    var second = sessions.Add("emulator-5560");
    var control = new FakeEmulatorControl();
    var reconnector = new FakeReconnectorDouble();
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => UseRestartFakes(services, sessions, control, reconnector));
    var client = Client(app);

    (await client.PostAsync(RestartUri(first.Id), null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
    await WaitForEndAsync(client, first.Id);

    (await client.GetAsync(RestartUri(second.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    control.RebootedIndexes.Should().ContainSingle().Which.Should().Be(2);
    reconnector.Calls.Select(c => c.SessionId).Should().OnlyContain(id => id == first.Id);
    var list = await ReadJsonAsync(await client.GetAsync(new Uri("/api/sessions/monitoring", UriKind.Relative)));
    var secondItem = list.GetProperty("sessions").EnumerateArray().Single(i => i.GetProperty("id").GetString() == second.Id);
    secondItem.GetProperty("state").GetString().Should().Be("live");
  }
}
