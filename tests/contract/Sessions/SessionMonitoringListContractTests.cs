#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.Liveness;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameBot.ContractTests.Sessions;

/// <summary>Feature 141 (US1, FR-002, FR-021): <c>GET /api/sessions/monitoring</c>.</summary>
public sealed class SessionMonitoringListContractTests : MonitoringContractTestBase {
  private static readonly Uri ListUri = new("/api/sessions/monitoring", UriKind.Relative);

  private static JsonElement[] Items(JsonElement root) => root.GetProperty("sessions").EnumerateArray().ToArray();

  [Fact]
  public async Task EmptyListGivesOkAndAnEmptyArray() {
    var sessions = new LivenessFakeSessionManager();
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
    });

    var resp = await Client(app).GetAsync(ListUri);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var root = await ReadJsonAsync(resp);
    root.GetProperty("sessions").GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task AllSessionsAreListedWithNameAndState() {
    var sessions = new LivenessFakeSessionManager();
    var live = sessions.Add("emulator-5558");
    var stopping = sessions.Add("emulator-5560");
    stopping.Status = SessionStatus.Stopping;
    var noSerial = sessions.Add(null);
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
      // The real ldconsole is not on the CI runner, so a fake keeps the restart check the same everywhere.
      services.RemoveAll<IEmulatorControl>();
      services.AddSingleton<IEmulatorControl>(new FakeEmulatorControl());
    });

    var resp = await Client(app).GetAsync(ListUri);

    resp.StatusCode.Should().Be(HttpStatusCode.OK);
    var items = Items(await ReadJsonAsync(resp));
    items.Should().HaveCount(3);
    var byId = items.ToDictionary(i => i.GetProperty("id").GetString()!);
    byId[live.Id].GetProperty("name").GetString().Should().Be("game-liveness (emulator-5558)");
    byId[live.Id].GetProperty("state").GetString().Should().Be("live");
    byId[live.Id].GetProperty("restartAvailable").GetBoolean().Should().BeTrue();
    byId[stopping.Id].GetProperty("state").GetString().Should().Be("stopping");
    byId[noSerial.Id].GetProperty("name").GetString().Should().Be("game-liveness");
    byId[noSerial.Id].GetProperty("deviceSerial").ValueKind.Should().Be(JsonValueKind.Null);
    byId[noSerial.Id].GetProperty("restartAvailable").GetBoolean().Should().BeFalse();
    byId[noSerial.Id].GetProperty("restartUnavailableReason").GetString().Should().Be("no_device_serial");
  }

  [Fact]
  public async Task AQueueSessionWithAnUnknownQueueShowsOnlyTheEmulator() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emulator-5560", "queue:cec14fb03d9f4919a7caa2b52bf885e9");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
    });

    var resp = await Client(app).GetAsync(ListUri);

    var item = Items(await ReadJsonAsync(resp)).Single();
    item.GetProperty("id").GetString().Should().Be(session.Id);
    item.GetProperty("name").GetString().Should().Be("emulator-5560");
  }

  [Fact]
  public async Task ASessionWithNoAnswerShowsTheNoAnswerStateAndReason() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emulator-5558");
    var liveness = new FixedLivenessService { Report = FixedLivenessService.NotLive(DeviceLivenessReasons.CaptureStalled) };
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
      services.RemoveAll<ISessionLivenessService>();
      services.AddSingleton<ISessionLivenessService>(liveness);
    });

    var resp = await Client(app).GetAsync(ListUri);

    var item = Items(await ReadJsonAsync(resp)).Single();
    item.GetProperty("id").GetString().Should().Be(session.Id);
    item.GetProperty("state").GetString().Should().Be("no_answer");
    item.GetProperty("reason").GetString().Should().Be(DeviceLivenessReasons.CaptureStalled);
  }

  [Fact]
  public async Task TheListRouteIsNotTreatedAsASessionIdAndRealIdsStillWork() {
    var sessions = new LivenessFakeSessionManager();
    var session = sessions.Add("emulator-5558");
    using var baseFactory = new WebApplicationFactory<Program>();
    using var app = Factory(baseFactory, services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(sessions);
      services.RemoveAll<ISessionTransportCheck>();
      services.AddSingleton<ISessionTransportCheck>(new FixedTransportCheck());
      services.RemoveAll<ISessionDirectCapture>();
      services.AddSingleton<ISessionDirectCapture>(new FixedDirectCapture());
    });
    var client = Client(app);

    var list = await client.GetAsync(ListUri);
    var listBody = await list.Content.ReadAsStringAsync();
    list.StatusCode.Should().Be(HttpStatusCode.OK, listBody);
    listBody.Should().NotContain("not_found");

    var single = await client.GetAsync(new Uri($"/api/sessions/{session.Id}", UriKind.Relative));
    single.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ReadJsonAsync(single)).GetProperty("id").GetString().Should().Be(session.Id);

    var snapshot = await client.GetAsync(new Uri($"/api/sessions/{session.Id}/snapshot", UriKind.Relative));
    snapshot.StatusCode.Should().Be(HttpStatusCode.OK);
    snapshot.Content.Headers.ContentType!.MediaType.Should().Be("image/png");

    var health = await client.GetAsync(new Uri($"/api/sessions/{session.Id}/health", UriKind.Relative));
    health.StatusCode.Should().Be(HttpStatusCode.OK);
    (await ReadJsonAsync(health)).GetProperty("id").GetString().Should().Be(session.Id);
  }
}
