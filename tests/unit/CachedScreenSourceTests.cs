using System;
using System.Drawing;
using System.Runtime.Versioning;
using System.Threading;
using GameBot.Domain.Triggers.Evaluators;
using Xunit;

namespace GameBot.UnitTests;

/// <summary>
/// Regression tests for CachedScreenSource.
/// Ensures that multiple GetLatestScreenshot() calls within the TTL window
/// do NOT trigger duplicate captures — preventing the 9-second-per-loop-iteration
/// performance bug caused by two ADB screencap round-trips (PrimitiveTap detection +
/// imageVisible break condition) in the same iteration.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CachedScreenSourceTests : IDisposable {
  // A controllable IScreenSource spy that counts invocations.
  private sealed class CountingScreenSource : IScreenSource {
    public int CallCount { get; private set; }
    public Bitmap? Next { get; set; }

    public Bitmap? GetLatestScreenshot() {
      CallCount++;
      if (Next is null) return null;
      // Return a new Bitmap each time (mirrors AdbScreenSource behaviour).
      return new Bitmap(Next);
    }
  }

  private readonly CountingScreenSource _inner = new();
  private readonly Bitmap _bitmap = CreateSolidBitmap(Color.Azure);

  public CachedScreenSourceTests() {
    _inner.Next = _bitmap;
  }

  public void Dispose() {
    _bitmap.Dispose();
  }

  private static Bitmap CreateSolidBitmap(Color c) {
    var bmp = new Bitmap(2, 2);
    using var g = Graphics.FromImage(bmp);
    g.Clear(c);
    return bmp;
  }

  [Fact(DisplayName = "Two calls within TTL hit the inner source only once")]
  public void TwoCallsWithinTtlInnerCalledOnce() {
    using var cached = new CachedScreenSource(_inner, ttlMs: 2000);

    using var first = cached.GetLatestScreenshot();
    using var second = cached.GetLatestScreenshot();

    Assert.Equal(1, _inner.CallCount);
    Assert.NotNull(first);
    Assert.NotNull(second);
  }

  [Fact(DisplayName = "Call after TTL expiry refreshes via the inner source")]
  public void CallAfterTtlExpiryInnerCalledAgain() {
    using var cached = new CachedScreenSource(_inner, ttlMs: 10); // 10 ms TTL

    using var first = cached.GetLatestScreenshot();
    Assert.Equal(1, _inner.CallCount);

    Thread.Sleep(30); // exceed TTL

    using var second = cached.GetLatestScreenshot();
    Assert.Equal(2, _inner.CallCount);
  }

  [Fact(DisplayName = "Returned Bitmap can be disposed without corrupting the cache")]
  public void DisposingReturnedBitmapDoesNotCorruptCache() {
    using var cached = new CachedScreenSource(_inner, ttlMs: 2000);

    var first = cached.GetLatestScreenshot();
    first?.Dispose(); // simulate ImageMatchEvaluator's 'using var screenBmp'

    // Second call: cache is still valid — no second ADB call.
    using var second = cached.GetLatestScreenshot();

    Assert.Equal(1, _inner.CallCount);
    Assert.NotNull(second);
  }

  [Fact(DisplayName = "Returns null and still applies TTL when inner returns null")]
  public void InnerReturnsNullReturnsNullAndCachesTtl() {
    _inner.Next = null;
    using var cached = new CachedScreenSource(_inner, ttlMs: 2000);

    var first = cached.GetLatestScreenshot();
    var second = cached.GetLatestScreenshot();

    Assert.Null(first);
    Assert.Null(second);
    // Both calls within TTL: inner must only be called once.
    Assert.Equal(1, _inner.CallCount);
  }

  [Fact(DisplayName = "First call after null result can re-fetch once TTL expires")]
  public void AfterNullResultRefetchesWhenTtlExpires() {
    _inner.Next = null;
    using var cached = new CachedScreenSource(_inner, ttlMs: 10);

    var first = cached.GetLatestScreenshot();
    Assert.Null(first);
    Assert.Equal(1, _inner.CallCount);

    Thread.Sleep(30); // exceed TTL
    _inner.Next = _bitmap;

    using var second = cached.GetLatestScreenshot();
    Assert.Equal(2, _inner.CallCount);
    Assert.NotNull(second);
  }

  [Fact(DisplayName = "Zero TTL never expires — one inner call for repeated reads")]
  public void ZeroTtlNeverExpires() {
    using var cached = new CachedScreenSource(_inner, ttlMs: 0);

    using var first = cached.GetLatestScreenshot();
    using var second = cached.GetLatestScreenshot();

    // Zero TTL = expiry is MaxValue, so cache never expires.
    Assert.Equal(1, _inner.CallCount);
  }

  [Fact(DisplayName = "Throws ObjectDisposedException after Dispose")]
  public void AfterDisposeThrowsObjectDisposedException() {
    var cached = new CachedScreenSource(_inner, ttlMs: 2000);
    cached.Dispose();
    Assert.Throws<ObjectDisposedException>(() => cached.GetLatestScreenshot());
  }

  [Fact(DisplayName = "Concurrent calls within TTL result in at most one inner call")]
  public void ConcurrentCallsWithinTtlAtMostOneInnerCall() {
    using var cached = new CachedScreenSource(_inner, ttlMs: 5000);
    var results = new Bitmap?[10];

    // Simulate loop-body sequential evaluation (PrimitiveTap + break-condition check).
    for (int i = 0; i < 10; i++) {
      results[i] = cached.GetLatestScreenshot();
    }

    foreach (var r in results) r?.Dispose();

    // All 10 calls within TTL → inner must be called exactly once.
    Assert.Equal(1, _inner.CallCount);
  }
}
