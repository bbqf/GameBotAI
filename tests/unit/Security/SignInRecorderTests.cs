using System.Text.Json;
using FluentAssertions;
using GameBot.Service.Security;
using GameBot.UnitTests.Queues;
using Xunit;

namespace GameBot.UnitTests.Security;

public sealed class SignInRecorderTests : IDisposable {
  private readonly string _dir = Path.Combine(Path.GetTempPath(), "gamebot-recorder-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

  public void Dispose() {
    if (Directory.Exists(_dir)) {
      Directory.Delete(_dir, recursive: true);
    }
  }

  private string[] Lines() => File.ReadAllLines(Path.Combine(_dir, "sign-in-records.jsonl"));

  [Fact]
  public void WritesOneJsonLineForEachEvent() {
    var recorder = new SignInRecorder(_dir, _clock);

    recorder.Record(SignInEvents.SignIn, "1.2.3.4", "abc12345");
    recorder.Record(SignInEvents.SignInFailed, "1.2.3.4", null, "no_passkey");

    var lines = Lines();
    lines.Should().HaveCount(2);
    using var doc = JsonDocument.Parse(lines[0]);
    doc.RootElement.GetProperty("event").GetString().Should().Be("sign_in");
    doc.RootElement.GetProperty("source").GetString().Should().Be("1.2.3.4");
    doc.RootElement.GetProperty("passkeyId").GetString().Should().Be("abc12345");
    doc.RootElement.GetProperty("timeUtc").GetString().Should().StartWith("2026-11-10T12:00:00");
  }

  [Fact]
  public void LineHasOnlyTheFixedFields() {
    var recorder = new SignInRecorder(_dir, _clock);
    recorder.Record(SignInEvents.SignIn, "src");

    using var doc = JsonDocument.Parse(Lines()[0]);
    doc.RootElement.EnumerateObject().Select(p => p.Name)
      .Should().BeEquivalentTo("timeUtc", "event", "source", "passkeyId", "detail");
  }

  [Fact]
  public void FileRollsAtOneMegabyte() {
    Directory.CreateDirectory(_dir);
    File.WriteAllText(Path.Combine(_dir, "sign-in-records.jsonl"), new string('x', (int)SignInRecorder.MaxFileBytes + 10));
    var recorder = new SignInRecorder(_dir, _clock);

    recorder.Record(SignInEvents.SignIn, "src");

    File.Exists(Path.Combine(_dir, "sign-in-records.1.jsonl")).Should().BeTrue();
    Lines().Should().HaveCount(1);
  }

  [Fact]
  public void RefusalIsWrittenOneTimeInTenMinutesForEachSource() {
    var recorder = new SignInRecorder(_dir, _clock);

    recorder.RecordRemoteAccessRefused("a");
    recorder.RecordRemoteAccessRefused("a");
    recorder.RecordRemoteAccessRefused("b");
    Lines().Should().HaveCount(2);

    _clock.Advance(TimeSpan.FromMinutes(11));
    recorder.RecordRemoteAccessRefused("a");
    Lines().Should().HaveCount(3);
  }

  [Fact]
  public void RefusalHasTheFixedInstructionText() {
    var recorder = new SignInRecorder(_dir, _clock);
    recorder.RecordRemoteAccessRefused("a");

    using var doc = JsonDocument.Parse(Lines()[0]);
    doc.RootElement.GetProperty("detail").GetString().Should().Contain("GAMEBOT_AUTH_ENABLED=true");
  }

  [Fact]
  public void ProxyRecordIsWrittenOneTimeInEachDay() {
    var recorder = new SignInRecorder(_dir, _clock);

    recorder.RecordProxyNoClientAddress("p");
    recorder.RecordProxyNoClientAddress("p");
    Lines().Should().HaveCount(1);

    _clock.Advance(TimeSpan.FromHours(25));
    recorder.RecordProxyNoClientAddress("p");
    Lines().Should().HaveCount(2);
  }
}
