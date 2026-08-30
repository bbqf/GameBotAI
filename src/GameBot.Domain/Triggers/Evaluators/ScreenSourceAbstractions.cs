using System;
using System.Drawing;
using System.Runtime.Versioning;

namespace GameBot.Domain.Triggers.Evaluators;

public interface IScreenSource {
  Bitmap? GetLatestScreenshot();
}

/// <summary>
/// Creates <see cref="IScreenSource"/> instances bound to one specific emulator session (feature 079).
/// </summary>
/// <remarks>
/// Used by every call site that already knows which session it is acting for, so concurrent queue runs
/// can never observe one another's screen. Paths that cannot take a session id (trigger evaluators,
/// condition adapters) keep using the singleton <see cref="IScreenSource"/>, which resolves the device
/// from the ambient <c>IDeviceContextAccessor</c> instead.
/// </remarks>
public interface IScreenSourceFactory {
  /// <summary>
  /// Returns a screen source that observes only <paramref name="sessionId"/> for its whole lifetime.
  /// When that session has no cached frame, or is no longer running, the source returns <c>null</c> —
  /// it never substitutes another session's frame.
  /// </summary>
  /// <param name="sessionId">The emulator session to observe. Must not be blank.</param>
  IScreenSource ForSession(string sessionId);
}

/// <summary>
/// <see cref="IScreenSourceFactory"/> that hands the same source back for every session (feature 079).
/// </summary>
/// <remarks>
/// Used in stub/test mode, where there is no real device and therefore nothing to keep separate, so
/// callers can depend on the factory unconditionally.
/// </remarks>
public sealed class FixedScreenSourceFactory : IScreenSourceFactory {
  private readonly IScreenSource _inner;

  /// <summary>Creates a factory that always returns <paramref name="inner"/>.</summary>
  public FixedScreenSourceFactory(IScreenSource inner) {
    _inner = inner ?? throw new ArgumentNullException(nameof(inner));
  }

  /// <inheritdoc />
  public IScreenSource ForSession(string sessionId) => _inner;
}

[SupportedOSPlatform("windows")]
public sealed class SingleBitmapScreenSource : IScreenSource {
  private readonly Func<Bitmap?> _provider;
  public SingleBitmapScreenSource(Func<Bitmap?> provider) { _provider = provider; }
  public Bitmap? GetLatestScreenshot() => _provider();
}

/// <summary>
/// Wraps an <see cref="IScreenSource"/> and caches the last screenshot for
/// <paramref name="ttlMs"/> milliseconds.  Subsequent calls within the TTL
/// window return a cheap in-memory clone instead of re-capturing via ADB,
/// preventing duplicate screencap round-trips in the same loop iteration.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CachedScreenSource : IScreenSource, IDisposable {
  private readonly IScreenSource _inner;
  private readonly int _ttlMs;
  private Bitmap? _cached;
  private DateTimeOffset _expiry = DateTimeOffset.MinValue;
  private readonly object _lock = new();
  private bool _disposed;

  public CachedScreenSource(IScreenSource inner, int ttlMs = 500) {
    _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    _ttlMs = Math.Max(0, ttlMs);
  }

  public Bitmap? GetLatestScreenshot() {
    lock (_lock) {
      ObjectDisposedException.ThrowIf(_disposed, this);

      if (DateTimeOffset.UtcNow < _expiry) {
        // Cache hit (may be null if inner returned null): return a clone or null.
        return _cached is not null ? new Bitmap(_cached) : null;
      }

      // Cache miss: capture from inner source and store a master copy.
      var fresh = _inner.GetLatestScreenshot();
      _cached?.Dispose();
      _cached = fresh;
      _expiry = _ttlMs > 0
        ? DateTimeOffset.UtcNow.AddMilliseconds(_ttlMs)
        : DateTimeOffset.MaxValue;

      return _cached is not null ? new Bitmap(_cached) : null;
    }
  }

  public void Dispose() {
    lock (_lock) {
      if (!_disposed) {
        _cached?.Dispose();
        _cached = null;
        _disposed = true;
      }
    }
  }
}
