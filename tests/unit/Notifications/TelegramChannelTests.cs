using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Notifications;
using GameBot.Service.Services.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1707, CA2000, CA5394

namespace GameBot.UnitTests.Notifications;

/// <summary>Feature 120: the Telegram channel with a fake HTTP handler.</summary>
public sealed class TelegramChannelTests {
  private const string Token = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcd-Xy9z";

  private sealed class FakeHandler : HttpMessageHandler {
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();
    public List<(Uri Uri, string Body, DateTime At)> Requests { get; } = new();

    public void Answer(Func<HttpRequestMessage, HttpResponseMessage> answer) => _answers.Enqueue(answer);

    public void Answer(HttpStatusCode status, string body = "{}") =>
      Answer(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
      var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
      Requests.Add((request.RequestUri!, body, DateTime.UtcNow));
      var answer = _answers.Count > 0 ? _answers.Dequeue() : (_ => new HttpResponseMessage(HttpStatusCode.OK));
      return answer(request);
    }
  }

  private sealed class HangingHandler : HttpMessageHandler {
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
      await Task.Delay(Timeout.Infinite, cancellationToken);
      return new HttpResponseMessage(HttpStatusCode.OK);
    }
  }

  private sealed class Factory : IHttpClientFactory {
    private readonly HttpMessageHandler _handler;

    public Factory(HttpMessageHandler handler) { _handler = handler; }

    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
  }

  private static TelegramChannel Channel(HttpMessageHandler handler, NotificationOptions? options = null) {
    options ??= new NotificationOptions { TelegramRetryPause = TimeSpan.FromMilliseconds(50) };
    return new TelegramChannel(new Factory(handler), Options.Create(options), NullLogger<TelegramChannel>.Instance);
  }

  private static NotificationTarget Target(string token = Token, string chatId = "-1001234567890") {
    var target = new NotificationTarget { Id = "t1", Type = "telegram", Name = "Phone" };
    target.Settings["botToken"] = token;
    target.Settings["chatId"] = chatId;
    return target;
  }

  [Fact]
  public async Task TheRequestHasTheUrlAndABodyWithChatIdAndTextAndNoParseMode() {
    var handler = new FakeHandler();
    var channel = Channel(handler);

    var result = await channel.SendAsync(Target(), "Farm : Seq : \U0001F534 failure", CancellationToken.None);

    result.Succeeded.Should().BeTrue();
    var request = handler.Requests.Should().ContainSingle().Subject;
    request.Uri.AbsoluteUri.Should().Be($"https://api.telegram.org/bot{Token}/sendMessage");
    using var doc = JsonDocument.Parse(request.Body);
    doc.RootElement.GetProperty("chat_id").GetString().Should().Be("-1001234567890");
    doc.RootElement.GetProperty("text").GetString().Should().Be("Farm : Seq : \U0001F534 failure");
    doc.RootElement.TryGetProperty("parse_mode", out _).Should().BeFalse();
  }

  [Fact]
  public async Task AFourHundredAnswerIsFinalWithTheDescriptionAndNoToken() {
    var handler = new FakeHandler();
    handler.Answer(HttpStatusCode.BadRequest, "{\"ok\":false,\"description\":\"Bad Request: chat not found\"}");
    var channel = Channel(handler);

    var result = await channel.SendAsync(Target(), "x", CancellationToken.None);

    handler.Requests.Should().HaveCount(1);
    result.Succeeded.Should().BeFalse();
    result.Reason.Should().Be("Telegram answered 400: Bad Request: chat not found");
    result.Reason.Should().NotContain(Token);
  }

  [Fact]
  public async Task AFiveHundredAnswerGivesTwoAttemptsWithAPause() {
    var handler = new FakeHandler();
    handler.Answer(HttpStatusCode.BadGateway, "not json");
    handler.Answer(HttpStatusCode.BadGateway, "{\"description\":\"Bad Gateway\"}");
    var channel = Channel(handler, new NotificationOptions { TelegramRetryPause = TimeSpan.FromMilliseconds(200) });

    var result = await channel.SendAsync(Target(), "x", CancellationToken.None);

    handler.Requests.Should().HaveCount(2);
    (handler.Requests[1].At - handler.Requests[0].At).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(150));
    result.Succeeded.Should().BeFalse();
    result.Reason.Should().Be("Telegram answered 502: Bad Gateway");
  }

  [Fact]
  public async Task TheSecondAttemptCanWork() {
    var handler = new FakeHandler();
    handler.Answer(HttpStatusCode.ServiceUnavailable);
    handler.Answer(HttpStatusCode.OK);
    var channel = Channel(handler);

    var result = await channel.SendAsync(Target(), "x", CancellationToken.None);

    result.Succeeded.Should().BeTrue();
    handler.Requests.Should().HaveCount(2);
  }

  [Fact]
  public async Task ANetworkFaultGivesAReasonWithTheTypeNameOnlyAndNoToken() {
    var handler = new FakeHandler();
    handler.Answer(_ => throw new HttpRequestException($"Cannot reach https://api.telegram.org/bot{Token}/sendMessage"));
    handler.Answer(_ => throw new HttpRequestException($"Cannot reach https://api.telegram.org/bot{Token}/sendMessage"));
    var channel = Channel(handler);

    var result = await channel.SendAsync(Target(), "x", CancellationToken.None);

    result.Succeeded.Should().BeFalse();
    result.Reason.Should().Be("Telegram did not answer: HttpRequestException");
    result.Reason.Should().NotContain(Token);
  }

  [Fact]
  public async Task AnUnexpectedExceptionNeverEscapesAndNeverShowsItsMessage() {
    var handler = new FakeHandler();
    handler.Answer(_ => throw new InvalidOperationException(Token));
    var channel = Channel(handler);

    var result = await channel.SendAsync(Target(), "x", CancellationToken.None);

    result.Succeeded.Should().BeFalse();
    result.Reason.Should().Be("Telegram did not answer: InvalidOperationException");
  }

  [Fact]
  public async Task AnAttemptThatHangsEndsAtTheAttemptLimit() {
    var channel = Channel(new HangingHandler(), new NotificationOptions {
      TelegramAttemptTimeout = TimeSpan.FromMilliseconds(100),
      TelegramRetryPause = TimeSpan.FromMilliseconds(10)
    });

    var result = await channel.SendAsync(Target(), "x", CancellationToken.None);

    result.Succeeded.Should().BeFalse();
    result.Reason.Should().Be("Telegram did not answer in time.");
  }

  [Fact]
  public async Task ACancelledSendGivesTheTimeReason() {
    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    var channel = Channel(new HangingHandler());

    var result = await channel.SendAsync(Target(), "x", cts.Token);

    result.Succeeded.Should().BeFalse();
    result.Reason.Should().Be("The target did not answer in time.");
  }

  [Fact]
  public async Task ATargetWithNoTokenFailsWithoutARequest() {
    var handler = new FakeHandler();
    var channel = Channel(handler);
    var target = new NotificationTarget { Id = "t", Type = "telegram", Name = "n" };

    var result = await channel.SendAsync(target, "x", CancellationToken.None);

    result.Succeeded.Should().BeFalse();
    handler.Requests.Should().BeEmpty();
  }

  [Fact]
  public async Task TheDescriptionHidesATokenThatTelegramShouldRepeat() {
    var handler = new FakeHandler();
    handler.Answer(HttpStatusCode.Unauthorized, $"{{\"description\":\"bad {Token} here\"}}");
    var channel = Channel(handler);

    var result = await channel.SendAsync(Target(), "x", CancellationToken.None);

    result.Reason.Should().NotContain(Token);
    result.Reason.Should().Contain("***");
  }

  [Theory]
  [InlineData("123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcd-Xy9z", "-1001", null)]
  [InlineData("123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcd-Xy9z", "@mychannel", null)]
  [InlineData("", "1", "botToken")]
  [InlineData("nonsense", "1", "botToken")]
  [InlineData("123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcd-Xy9z", "", "chatId")]
  [InlineData("123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ_abcd-Xy9z", "abc", "chatId")]
  public void ValidateChecksTheFieldShapes(string token, string chatId, string? badField) {
    var channel = Channel(new FakeHandler());
    ArgumentNullException.ThrowIfNull(token);
    var target = new NotificationTarget { Type = "telegram", Name = "n" };
    target.Settings["botToken"] = token;
    target.Settings["chatId"] = chatId;

    var error = channel.Validate(target);

    if (badField is null) error.Should().BeNull();
    else error.Should().Contain(badField).And.NotContain(token.Length > 3 ? token : "\u0001");
  }

  [Fact]
  public void ValidateRejectsAnUnknownKey() {
    var channel = Channel(new FakeHandler());
    var target = Target();
    target.Settings["extra"] = "x";

    channel.Validate(target).Should().Contain("extra");
  }

  [Fact]
  public void TheFieldListNamesTheSecretField() {
    var channel = Channel(new FakeHandler());

    channel.Type.Should().Be("telegram");
    channel.DisplayName.Should().Be("Telegram");
    channel.Fields.Where(f => f.Secret).Select(f => f.Key).Should().Equal("botToken");
    channel.Fields.Select(f => f.Key).Should().Equal("botToken", "chatId");
  }
}
