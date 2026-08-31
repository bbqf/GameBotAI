using System;

namespace GameBot.Domain.Sessions;

/// <summary>
/// Ambient "which device is this execution flow acting on" context (feature 079).
/// </summary>
/// <remarks>
/// A queue run pushes its <see cref="DeviceContext"/> around each sequence firing; anything running
/// inside that flow — nested sequences, commands, loops, trigger-based image/text conditions — sees it
/// without the session id having to be threaded through <c>ITriggerEvaluator</c> and every evaluator.
/// Outside any run the context is <c>null</c>, and consumers fall back to the single-running-session
/// rule.
/// </remarks>
public interface IDeviceContextAccessor {
  /// <summary>
  /// The device context of the current execution flow, or <c>null</c> when none has been pushed.
  /// </summary>
  DeviceContext? Current { get; }

  /// <summary>
  /// Sets <see cref="Current"/> for the current execution flow (and every flow it starts) until the
  /// returned scope is disposed, which restores the previous value.
  /// </summary>
  /// <param name="context">The context to make current. Must not be null.</param>
  /// <returns>A scope that restores the previous context on dispose. Disposing twice is a no-op.</returns>
  /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
  IDisposable Push(DeviceContext context);
}
