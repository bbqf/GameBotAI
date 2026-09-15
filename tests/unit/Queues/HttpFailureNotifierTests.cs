using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

// CA2000: every handler and HttpClient below is scoped to a single test method and is released with
// it. Disposing them explicitly would mean a using-declaration per handler in twelve tests for no
// behavioural difference, and the constitution permits disabling code-quality rules in test code.
#pragma warning disable CA2000

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Delivery behaviour of the HTTP failure notifier (feature 087, issue #181).
/// <para>
/// The load-bearing assertion in this file is not that a notification arrives — it is that
/// <b>nothing ever escapes as an exception</b>. The notifier is called from the run loop that
/// drives production farms, so an unreachable alert receiver must cost a run nothing at all.
/// </para>
/// </summary>
public class HttpFailureNotifierTests {
  private static readonly DateTimeOffset T0 =
    new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

  /// <summary>A handler that answers from a script, recording every request it is given.</summary>
  private sealed class ScriptedHandler : HttpMessageHandler {
    private readonly Func<HttpRequestMessage, int, HttpResponseMessage> _respond;
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string> Bodies { get; } = new();

    public ScriptedHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) {
      _respond = respond;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken) {
      var attempt = Requests.Count + 1;
      Requests.Add(request);
      Bodies.Add(request.Content is null
        ? string.Empty
        : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
      return _respond(request, attempt);
    }
  }

  private static HttpFailureNotifier NewNotifier(
    HttpMessageHandler handler, FailureNotificationOptions? options = null) =>
    new HttpFailureNotifier(
      new HttpClient(handler),
      Options.Create(options ?? new FailureNotificationOptions {
        DefaultUrl = "http://localhost:9099/alerts"
      }),
      NullLogger<HttpFailureNotifier>.Instance,
      new FakeTimeProvider(T0));

  private static FailureNotificationEvent NewEvent() => new FailureNotificationEvent {
    RaisedAt = T0,
    QueueId = "q1",
    QueueName = "PNS Daily 5558",
    EmulatorSerial = "emulator-5558",
    ConsecutiveFailedCycles = 5,
    CyclesCompleted = 417,
    FailedEntryIndex = 2,
    FailedSequenceId = "seq-alliance-help",
    FailedSequenceName = "PNS.DonateAllianceTech",
    FailedEntryCount = 2,
    Action = "notify",
    Message = "Queue 'PNS Daily 5558' has failed 5 consecutive cycles; action: notify."
  };

  private static ScriptedHandler Always(HttpStatusCode code) =>
    new ScriptedHandler((_, _) => new HttpResponseMessage(code));

  [Fact]
  public async Task Success_PostsOnceAndReportsSuccess() {
    var handler = Always(HttpStatusCode.OK);
    var result = await NewNotifier(handler).NotifyAsync(NewEvent(), null).ConfigureAwait(false);

    result.Succeeded.Should().BeTrue();
    result.Error.Should().BeNull();
    handler.Requests.Should().ContainSingle();
    handler.Requests[0].Method.Should().Be(HttpMethod.Post);
  }

  [Fact]
  public async Task PayloadMatchesThePublishedContract() {
    var handler = Always(HttpStatusCode.OK);
    await NewNotifier(handler).NotifyAsync(NewEvent(), null).ConfigureAwait(false);

    using var doc = JsonDocument.Parse(handler.Bodies[0]);
    var root = doc.RootElement;
    root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
    root.GetProperty("eventType").GetString().Should().Be("queue.failure-policy");
    root.GetProperty("queueId").GetString().Should().Be("q1");
    root.GetProperty("queueName").GetString().Should().Be("PNS Daily 5558");
    root.GetProperty("emulatorSerial").GetString().Should().Be("emulator-5558");
    root.GetProperty("consecutiveFailedCycles").GetInt32().Should().Be(5);
    root.GetProperty("cyclesCompleted").GetInt32().Should().Be(417);
    root.GetProperty("failedEntryIndex").GetInt32().Should().Be(2);
    root.GetProperty("failedSequenceId").GetString().Should().Be("seq-alliance-help");
    root.GetProperty("failedSequenceName").GetString().Should().Be("PNS.DonateAllianceTech");
    root.GetProperty("failedEntryCount").GetInt32().Should().Be(2);
    root.GetProperty("action").GetString().Should().Be("notify");
    root.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task AuthHeader_IsSentOnlyWhenConfigured() {
    var without = Always(HttpStatusCode.OK);
    await NewNotifier(without).NotifyAsync(NewEvent(), null).ConfigureAwait(false);
    without.Requests[0].Headers.Contains("X-GameBot-Token").Should().BeFalse();

    var with = Always(HttpStatusCode.OK);
    await NewNotifier(with, new FailureNotificationOptions {
      DefaultUrl = "http://localhost:9099/alerts",
      AuthHeaderName = "X-GameBot-Token",
      AuthHeaderValue = "s3cret"
    }).NotifyAsync(NewEvent(), null).ConfigureAwait(false);

    with.Requests[0].Headers.GetValues("X-GameBot-Token").Should().ContainSingle().Which.Should().Be("s3cret");
  }

  [Fact]
  public async Task OverrideUrl_WinsOverTheDefault() {
    var handler = Always(HttpStatusCode.OK);
    await NewNotifier(handler)
      .NotifyAsync(NewEvent(), "https://alerts.example/hook")
      .ConfigureAwait(false);

    handler.Requests[0].RequestUri!.ToString().Should().Be("https://alerts.example/hook");
  }

  [Fact]
  public async Task NonSuccessStatus_IsRetriedOnceThenAbandoned() {
    var handler = Always(HttpStatusCode.InternalServerError);
    var result = await NewNotifier(handler).NotifyAsync(NewEvent(), null).ConfigureAwait(false);

    result.Succeeded.Should().BeFalse();
    result.Error.Should().Contain("500");
    handler.Requests.Should().HaveCount(2, "the budget is 2 attempts total, not 2 retries");
  }

  [Fact]
  public async Task RetrySucceeding_ReportsSuccess() {
    var handler = new ScriptedHandler((_, attempt) =>
      new HttpResponseMessage(attempt == 1 ? HttpStatusCode.BadGateway : HttpStatusCode.OK));

    var result = await NewNotifier(handler).NotifyAsync(NewEvent(), null).ConfigureAwait(false);

    result.Succeeded.Should().BeTrue();
    handler.Requests.Should().HaveCount(2);
  }

  [Fact]
  public async Task MaxAttempts_IsHonoured() {
    var handler = Always(HttpStatusCode.ServiceUnavailable);
    var result = await NewNotifier(handler, new FailureNotificationOptions {
      DefaultUrl = "http://localhost:9099/alerts",
      MaxAttempts = 1
    }).NotifyAsync(NewEvent(), null).ConfigureAwait(false);

    result.Succeeded.Should().BeFalse();
    handler.Requests.Should().ContainSingle();
  }

  [Fact]
  public async Task NoDestinationConfigured_FailsCleanlyWithoutSending() {
    var handler = Always(HttpStatusCode.OK);
    var result = await NewNotifier(handler, new FailureNotificationOptions())
      .NotifyAsync(NewEvent(), null).ConfigureAwait(false);

    result.Succeeded.Should().BeFalse();
    result.Error.Should().Contain("no destination");
    handler.Requests.Should().BeEmpty();
  }

  /// <summary>The contract that matters most: a throwing transport is a result, never an exception.</summary>
  [Fact]
  public async Task ThrowingTransport_NeverPropagates() {
    var handler = new ScriptedHandler((_, _) => throw new HttpRequestException("connection refused"));

    var result = await NewNotifier(handler).NotifyAsync(NewEvent(), null).ConfigureAwait(false);

    result.Succeeded.Should().BeFalse();
    result.Error.Should().Contain("connection refused");
  }

  [Fact]
  public async Task MalformedDestination_FailsCleanly() {
    var handler = Always(HttpStatusCode.OK);
    var result = await NewNotifier(handler)
      .NotifyAsync(NewEvent(), "not-a-url")
      .ConfigureAwait(false);

    result.Succeeded.Should().BeFalse();
    result.Error.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task HangingReceiver_IsAbandonedAtTheTimeout() {
    var handler = new ScriptedHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
    var slow = new SlowHandler(TimeSpan.FromSeconds(30));

    var started = DateTimeOffset.UtcNow;
    var result = await NewNotifier(slow, new FailureNotificationOptions {
      DefaultUrl = "http://localhost:9099/alerts",
      TimeoutSeconds = 1,
      MaxAttempts = 1
    }).NotifyAsync(NewEvent(), null).ConfigureAwait(false);
    var elapsed = DateTimeOffset.UtcNow - started;

    result.Succeeded.Should().BeFalse();
    result.Error.Should().Contain("timed out");
    elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the timeout must bound the wait");
    handler.Requests.Should().BeEmpty();
  }

  private sealed class SlowHandler : HttpMessageHandler {
    private readonly TimeSpan _delay;
    public SlowHandler(TimeSpan delay) { _delay = delay; }

    protected override async Task<HttpResponseMessage> SendAsync(
      HttpRequestMessage request, CancellationToken cancellationToken) {
      await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
      return new HttpResponseMessage(HttpStatusCode.OK);
    }
  }
}
