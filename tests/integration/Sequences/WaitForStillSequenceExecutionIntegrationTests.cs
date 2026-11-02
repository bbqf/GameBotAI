using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA2000, CA1416

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 174: a full sequence run with a WaitForStill step through the real service, with a fake frame source.
/// The step ends with still on a screen that stops, and with timeout_elapsed on a screen that never stops. A region
/// ignores a moving part outside it. A later step can branch on the outcome.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class WaitForStillSequenceExecutionIntegrationTests : IDisposable {
  private const string Session = "fake-session";

  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevAuthToken;
  private readonly string? _prevDataDir;
  private readonly string? _prevStorageRoot;

  public WaitForStillSequenceExecutionIntegrationTests() {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    _prevStorageRoot = Environment.GetEnvironmentVariable("Service__Storage__Root");

    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  public void Dispose() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _prevStorageRoot);
    GC.SuppressFinalize(this);
  }

  // The screen: a 40x30 picture. The top-left block (inside a small region) and the bottom-right block (outside it)
  // can each flip with every capture, or stay the same. A block is big enough to pass the default maxDiff of 0.01.
  private sealed class FakeScreen : ISessionFrameSource {
    private int _calls;

    public bool FlipInside { get; set; }

    public bool FlipOutside { get; set; }

    public Bitmap? Capture(string sessionId) {
      var n = Interlocked.Increment(ref _calls);
      var bitmap = new Bitmap(40, 30);
      using (var g = Graphics.FromImage(bitmap)) {
        g.Clear(Color.Black);
      }

      using (var g = Graphics.FromImage(bitmap)) {
        if (FlipInside && n % 2 == 0) g.FillRectangle(Brushes.White, 0, 0, 20, 15);
        if (FlipOutside && n % 2 == 0) g.FillRectangle(Brushes.White, 20, 15, 20, 15);
      }

      return bitmap;
    }
  }

  private static WebApplicationFactory<Program> NewApp(FakeScreen screen) =>
    new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
      builder.ConfigureServices(services => {
        services.RemoveAll<ISessionFrameSource>();
        services.AddSingleton<ISessionFrameSource>(screen);
      }));

  private static HttpClient NewClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static object WaitStep(object payload) => new {
    stepId = "wait",
    stepType = "Action",
    primitiveAction = new { type = "WaitForStill", schemaVersion = "1", payload }
  };

  private static object Guarded(string id, string expectedState) => new {
    stepId = id,
    stepType = "Action",
    primitiveAction = new { type = "notify", schemaVersion = "1", payload = new { message = "branch " + id } },
    condition = new { type = "commandOutcome", stepRef = "wait", expectedState }
  };

  private static async Task<JsonElement> RunAsync(HttpClient client, params object[] steps) {
    var create = await client.PostAsJsonAsync("/api/sequences", new {
      name = "wait-still-" + Guid.NewGuid().ToString("N"),
      version = 1,
      steps
    }).ConfigureAwait(false);
    var created = await create.Content.ReadAsStringAsync().ConfigureAwait(false);
    create.StatusCode.Should().Be(HttpStatusCode.Created, created);
    var id = JsonDocument.Parse(created).RootElement.GetProperty("id").GetString()!;

    var run = await client.PostAsJsonAsync($"/api/sequences/{id}/execute", new { sessionId = Session }).ConfigureAwait(false);
    var body = await run.Content.ReadAsStringAsync().ConfigureAwait(false);
    run.StatusCode.Should().Be(HttpStatusCode.OK, body);
    return JsonDocument.Parse(body).RootElement.Clone();
  }

  private static JsonElement StepOf(JsonElement run, string stepId) =>
    run.GetProperty("steps").EnumerateArray().First(s => s.GetProperty("commandId").GetString() == stepId);

  private static string? Outcome(JsonElement run, string stepId) =>
    StepOf(run, stepId).GetProperty("actionOutcome").GetString();

  private static bool Ran(JsonElement run, string stepId) {
    var step = run.GetProperty("steps").EnumerateArray()
      .FirstOrDefault(s => s.GetProperty("commandId").GetString() == stepId);
    return step.ValueKind != JsonValueKind.Undefined && step.GetProperty("actionOutcome").GetString() != "skipped";
  }

  [Fact]
  public async Task AScreenThatStopsEndsWithStill() {
    using var app = NewApp(new FakeScreen());
    var client = NewClient(app);

    var run = await RunAsync(client, WaitStep(new { stableMs = 300, timeoutMs = 4000 })).ConfigureAwait(false);

    run.GetProperty("status").GetString().Should().Be("Succeeded");
    Outcome(run, "wait").Should().Be("still");
    StepOf(run, "wait").GetProperty("message").GetString().Should().StartWith("still after ");
  }

  [Fact]
  public async Task AScreenThatNeverStopsEndsWithTimeoutElapsed() {
    using var app = NewApp(new FakeScreen { FlipInside = true });
    var client = NewClient(app);

    var run = await RunAsync(client, WaitStep(new { stableMs = 300, timeoutMs = 1200 })).ConfigureAwait(false);

    run.GetProperty("status").GetString().Should().Be("Succeeded");
    Outcome(run, "wait").Should().Be("timeout_elapsed");
    StepOf(run, "wait").GetProperty("message").GetString().Should().StartWith("not still after 1200 ms");
  }

  [Fact]
  public async Task AMovingPartOutsideTheRegionIsIgnored() {
    using var app = NewApp(new FakeScreen { FlipOutside = true });
    var client = NewClient(app);

    var run = await RunAsync(client, WaitStep(new {
      region = new { x = 0, y = 0, width = 10, height = 10 }, stableMs = 300, timeoutMs = 4000
    })).ConfigureAwait(false);

    Outcome(run, "wait").Should().Be("still");
  }

  [Fact]
  public async Task AMovingPartInsideTheRegionKeepsTheStepWaiting() {
    using var app = NewApp(new FakeScreen { FlipInside = true });
    var client = NewClient(app);

    var run = await RunAsync(client, WaitStep(new {
      region = new { x = 0, y = 0, width = 10, height = 10 }, stableMs = 300, timeoutMs = 1200
    })).ConfigureAwait(false);

    Outcome(run, "wait").Should().Be("timeout_elapsed");
  }

  [Fact]
  public async Task ARegionOutsideTheScreenFailsTheStep() {
    using var app = NewApp(new FakeScreen());
    var client = NewClient(app);

    var run = await RunAsync(client, WaitStep(new {
      region = new { x = 500, y = 500, width = 10, height = 10 }, timeoutMs = 2000
    })).ConfigureAwait(false);

    run.GetProperty("status").GetString().Should().NotBe("Succeeded");
    Outcome(run, "wait").Should().Be("failed");
    StepOf(run, "wait").GetProperty("message").GetString().Should().Be("region is outside the screen (40x30)");
  }

  [Fact]
  public async Task ALaterStepBranchesOnStill() {
    using var app = NewApp(new FakeScreen());
    var client = NewClient(app);

    var run = await RunAsync(client,
      WaitStep(new { stableMs = 300, timeoutMs = 4000 }),
      Guarded("on-still", "still"),
      Guarded("on-timeout", "timeout_elapsed")).ConfigureAwait(false);

    Ran(run, "on-still").Should().BeTrue();
    Ran(run, "on-timeout").Should().BeFalse();
  }

  [Fact]
  public async Task ALaterStepBranchesOnTimeoutElapsed() {
    using var app = NewApp(new FakeScreen { FlipInside = true });
    var client = NewClient(app);

    var run = await RunAsync(client,
      WaitStep(new { stableMs = 300, timeoutMs = 1200 }),
      Guarded("on-still", "still"),
      Guarded("on-timeout", "timeout_elapsed")).ConfigureAwait(false);

    Ran(run, "on-still").Should().BeFalse();
    Ran(run, "on-timeout").Should().BeTrue();
  }
}
