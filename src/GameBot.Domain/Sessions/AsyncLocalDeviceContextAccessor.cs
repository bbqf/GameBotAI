using System;
using System.Threading;

namespace GameBot.Domain.Sessions;

/// <summary>
/// Default <see cref="IDeviceContextAccessor"/>: an <see cref="AsyncLocal{T}"/> holding the current
/// <see cref="DeviceContext"/> (feature 079).
/// </summary>
/// <remarks>
/// <see cref="AsyncLocal{T}"/> flows with the ExecutionContext, so the value survives every
/// <c>await</c> inside a queue run and is captured by the <c>Task.Run</c> that launches the run. A
/// value set inside a child flow does not leak back to its parent, so a nested push cannot outlive
/// the scope that made it.
/// </remarks>
public sealed class AsyncLocalDeviceContextAccessor : IDeviceContextAccessor {
  private static readonly AsyncLocal<DeviceContext?> Ambient = new();

  /// <inheritdoc />
  public DeviceContext? Current => Ambient.Value;

  /// <inheritdoc />
  public IDisposable Push(DeviceContext context) {
    ArgumentNullException.ThrowIfNull(context);
    var previous = Ambient.Value;
    Ambient.Value = context;
    return new Scope(previous);
  }

  /// <summary>Restores the context that was current before the matching <see cref="Push"/>.</summary>
  private sealed class Scope : IDisposable {
    private readonly DeviceContext? _previous;
    private bool _disposed;

    public Scope(DeviceContext? previous) => _previous = previous;

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;
      Ambient.Value = _previous;
    }
  }
}
