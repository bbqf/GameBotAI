using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// The one recovery slot of the service (feature 121, FR-011, FR-012). It starts at most one instance
/// reboot at a time. The gap between two reboot start times is <c>RecoveryStaggerMs</c> or more.
/// </summary>
internal interface IDeviceRecoveryCoordinator {
  /// <summary>
  /// Restarts the LDPlayer instance <paramref name="instanceName"/> when the slot is free and the
  /// stagger time has passed. Callers for the same instance name (case-insensitive) share one reboot.
  /// Returns true when the reboot command ended with exit code 0. Returns false for each other result,
  /// also when the command throws. A cancel of <paramref name="ct"/> throws
  /// <see cref="OperationCanceledException"/>. The slot is free after a cancel or a failure.
  /// </summary>
  /// <param name="instanceName">The LDPlayer instance name of the queue.</param>
  /// <param name="ct">The token of the queue run.</param>
  Task<bool> RebootInstanceAsync(string instanceName, CancellationToken ct);
}
