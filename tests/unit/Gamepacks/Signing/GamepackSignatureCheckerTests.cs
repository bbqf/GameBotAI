using System;
using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Gamepacks.Signing;
using Microsoft.Extensions.Logging;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks.Signing;

/// <summary>Feature 146 (T015): the checker, the wouldReject rule, and the log record (FR-016).</summary>
public sealed class GamepackSignatureCheckerTests : IDisposable {
  private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GameBotSigning", Guid.NewGuid().ToString("N"));
  private readonly TestSigner _signer = new();
  private readonly CaptureLogger _logger = new();

  public GamepackSignatureCheckerTests() => System.IO.Directory.CreateDirectory(_root);

  public void Dispose() {
    _signer.Dispose();
    try {
      System.IO.Directory.Delete(_root, recursive: true);
    }
    catch (System.IO.IOException) {
      // A temporary folder that stays is not a test failure.
    }
  }

  private sealed class CountingVerifier : IGamepackSignatureVerifier {
    public int Calls { get; private set; }

    public SignatureStatus Verify(string gamepackJson, string? signatureText, IReadOnlyCollection<TrustedKey> trustedKeys) {
      Calls++;
      return new GamepackSignatureVerifier().Verify(gamepackJson, signatureText, trustedKeys);
    }
  }

  private sealed class CaptureLogger : ILogger<GamepackSignatureChecker> {
    public List<string> Messages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
      Messages.Add(formatter(state, exception));
  }

  private static string Pack() => GamepackTestKit.Pack("pns-daily", "1.0.0");

  [Theory]
  [InlineData(SignaturePolicy.Required, true)]
  [InlineData(SignaturePolicy.Optional, false)]
  public void WouldRejectIsTrueOnlyForRequiredWithABadStatus(SignaturePolicy policy, bool expected) {
    var store = new GamepackSignatureSettingsStore(_root);
    store.SetPolicy(policy);
    var checker = new GamepackSignatureChecker(store, new GamepackSignatureVerifier(), _logger);

    var bad = checker.Check(Pack(), null);
    bad.Status.Should().Be(SignatureStatus.Missing);
    bad.Policy.Should().Be(policy);
    bad.WouldReject.Should().Be(expected);

    store.AddKey("A", _signer.PublicKey);
    var good = checker.Check(Pack(), _signer.Sign(Pack()));
    good.Status.Should().Be(SignatureStatus.Valid);
    good.WouldReject.Should().BeFalse();
  }

  [Fact]
  public void PolicyOffNeverRunsTheVerifier() {
    var store = new GamepackSignatureSettingsStore(_root);
    var spy = new CountingVerifier();
    var checker = new GamepackSignatureChecker(store, spy, _logger);

    var result = checker.Check(Pack(), _signer.Sign(Pack()));

    result.Status.Should().Be(SignatureStatus.NotChecked);
    result.WouldReject.Should().BeFalse();
    spy.Calls.Should().Be(0);
  }

  [Fact]
  public void TheLogRecordHoldsStatusAndPolicyAndNoKeyText() {
    var store = new GamepackSignatureSettingsStore(_root);
    store.SetPolicy(SignaturePolicy.Required);
    store.AddKey("Secret label", _signer.PublicKey);
    var checker = new GamepackSignatureChecker(store, new GamepackSignatureVerifier(), _logger);

    checker.Check(Pack(), _signer.Sign(Pack()));

    var message = _logger.Messages.Should().ContainSingle().Subject;
    message.Should().Contain("valid").And.Contain("required");
    message.Should().NotContain(_signer.PublicKey).And.NotContain(_signer.Fingerprint).And.NotContain("Secret label");
  }
}
