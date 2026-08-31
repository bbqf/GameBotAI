using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 079 (FR-006/FR-007): the one rule that decides which emulator a step acts on.
///
/// Before this feature every call site inlined "exactly one running session, else fail", so the
/// moment a second queue started, steps in the <i>first</i> queue began failing with
/// "no session available" even though they carried an explicit session.
/// </summary>
public sealed class SessionResolutionTests {
  [Fact]
  public void AnExplicitSessionWinsEvenWithSeveralSessionsRunning() {
    var sessions = new FakeSessions("s1", "s2", "s3");

    SessionResolver.TryResolve(sessions, "s2", "tap", out var resolved, out var error).Should().BeTrue();

    resolved.Should().Be("s2");
    error.Should().BeEmpty();
  }

  [Fact]
  public void AnExplicitSessionWinsWithNoSessionsListedAtAll() {
    // The caller owns the session; the resolver must not second-guess the listing.
    var sessions = new FakeSessions();

    SessionResolver.TryResolve(sessions, "s1", "tap", out var resolved, out _).Should().BeTrue();

    resolved.Should().Be("s1");
  }

  [Fact]
  public void WithNoSessionSuppliedAndExactlyOneRunningThatOneIsUsed() {
    var sessions = new FakeSessions("only");

    SessionResolver.TryResolve(sessions, null, "tap", out var resolved, out var error).Should().BeTrue();

    resolved.Should().Be("only");
    error.Should().BeEmpty();
  }

  [Fact]
  public void WithNoSessionSuppliedAndNoneRunningTheMessageAsksForOne() {
    var sessions = new FakeSessions();

    SessionResolver.TryResolve(sessions, null, "go-to-home-screen", out var resolved, out var error).Should().BeFalse();

    resolved.Should().BeNull();
    error.Should().Be("no session available for 'go-to-home-screen' step; start a session or pass a sessionId");
  }

  [Fact]
  public void WithNoSessionSuppliedAndSeveralRunningTheStepFailsNamingTheCount() {
    var sessions = new FakeSessions("s1", "s2", "s3");

    SessionResolver.TryResolve(sessions, null, "ensure-game-running", out var resolved, out var error).Should().BeFalse();

    resolved.Should().BeNull("guessing a device is exactly the defect this feature fixes");
    error.Should().Be("3 device sessions are active; specify a sessionId for 'ensure-game-running'");
  }

  [Theory]
  [InlineData("")]
  [InlineData("   ")]
  public void ABlankSessionIdCountsAsNoSession(string sessionId) {
    var sessions = new FakeSessions("only");

    SessionResolver.TryResolve(sessions, sessionId, "swipe", out var resolved, out _).Should().BeTrue();

    resolved.Should().Be("only");
  }

  [Fact]
  public void StoppedSessionsDoNotCountTowardsTheRunningTotal() {
    var sessions = new FakeSessions("live");
    sessions.SeedStopped("dead-1", "dead-2");

    SessionResolver.TryResolve(sessions, null, "key", out var resolved, out _).Should().BeTrue();

    resolved.Should().Be("live");
  }

  [Fact]
  public void TheAmbiguityMessageNamesTheStep() {
    SessionResolver.Ambiguous(2, "waitForImage")
      .Should().Be("2 device sessions are active; specify a sessionId for 'waitForImage'");
  }
}

/// <summary>Minimal <see cref="ISessionManager"/> that only has to list sessions.</summary>
file sealed class FakeSessions : ISessionManager {
  private readonly List<EmulatorSession> _sessions = new();

  public FakeSessions(params string[] runningIds) {
    foreach (var id in runningIds) {
      _sessions.Add(new EmulatorSession { Id = id, GameId = "g", Status = SessionStatus.Running, DeviceSerial = $"dev-{id}" });
    }
  }

  public void SeedStopped(params string[] ids) {
    foreach (var id in ids) {
      _sessions.Add(new EmulatorSession { Id = id, GameId = "g", Status = SessionStatus.Stopped, DeviceSerial = $"dev-{id}" });
    }
  }

  public int ActiveCount => _sessions.Count;
  public bool CanCreateSession => true;
  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();
  public EmulatorSession? GetSession(string id) => _sessions.Find(s => s.Id == id);
  public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions;
  public bool StopSession(string id) => _sessions.RemoveAll(s => s.Id == id) > 0;
  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}
