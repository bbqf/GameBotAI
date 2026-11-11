using System.Diagnostics;
using FluentAssertions;
using GameBot.Service.Security;
using Microsoft.AspNetCore.Http;
using Xunit;
using Xunit.Abstractions;

namespace GameBot.UnitTests.Performance;

/// <summary>The access gate adds less than 1 ms (p95) to a request and writes no file on a usual request (feature 187, plan "Performance Goals").</summary>
public sealed class AccessGatePerformanceTests : IDisposable {
  private readonly string _dir = Path.Combine(Path.GetTempPath(), "gamebot-gateperf-" + Guid.NewGuid().ToString("N"));
  private readonly ITestOutputHelper _output;

  public AccessGatePerformanceTests(ITestOutputHelper output) {
    _output = output;
  }

  public void Dispose() {
    if (Directory.Exists(_dir)) {
      Directory.Delete(_dir, recursive: true);
    }

    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task SessionCheckThroughTheGateIsBelowOneMillisecondAndWritesNoFile() {
    var options = AuthOptions.Create("true", "https://bot.example.com", "192.168.1.5", "token");
    var recorder = new SignInRecorder(_dir, TimeProvider.System);
    var passkeys = new PasskeyStore(_dir, TimeProvider.System);
    passkeys.Add("cred-1", "key", 0, "x", out var added);
    var sessions = new SessionStore(_dir, passkeys, TimeProvider.System);
    var token = sessions.Create(added!.Id);
    var gate = new AccessGateMiddleware(
      _ => Task.CompletedTask,
      options,
      new RequestOriginClassifier(options),
      new ClientAddressResolver(options, recorder),
      sessions,
      new FailedAttemptLimiter(TimeProvider.System),
      recorder);

    HttpContext NewContext() {
      var context = new DefaultHttpContext();
      context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("192.168.1.5");
      context.Request.Path = "/api/queues";
      context.Request.Method = "GET";
      context.Request.Headers.Cookie = SessionStore.CookieName + "=" + token;
      return context;
    }

    var sessionsFile = Path.Combine(_dir, "sessions.json");
    for (var i = 0; i < 200; i++) {
      await gate.InvokeAsync(NewContext()).ConfigureAwait(true);
    }

    var writeTime = File.GetLastWriteTimeUtc(sessionsFile);
    var samples = new List<double>();
    for (var i = 0; i < 2000; i++) {
      var context = NewContext();
      var start = Stopwatch.GetTimestamp();
      await gate.InvokeAsync(context).ConfigureAwait(true);
      samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
      context.Response.StatusCode.Should().Be(200);
    }

    samples.Sort();
    var p95 = samples[(int)(samples.Count * 0.95)];
    _output.WriteLine($"Access gate session check: p95 {p95:F4} ms, max {samples[^1]:F4} ms (n={samples.Count}).");
    p95.Should().BeLessThan(1.0, "the gate must add less than 1 ms (p95) to a request");
    File.GetLastWriteTimeUtc(sessionsFile).Should().Be(writeTime, "a usual request must not write a file");
  }
}
