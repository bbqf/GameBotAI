using System;
using System.Collections.Concurrent;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Default <see cref="IDeviceClaimRegistry"/>: a singleton owning a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> of <see cref="DeviceClaim"/>s keyed by normalized
/// emulator serial (feature 079).
/// </summary>
/// <remarks>
/// <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> makes claiming atomic, so two simultaneous
/// starts for the same device cannot both succeed (FR-012) without any lock. Holds no service
/// dependencies and no persisted state.
/// </remarks>
internal sealed class DeviceClaimRegistry : IDeviceClaimRegistry {
  private readonly ConcurrentDictionary<string, DeviceClaim> _claims =
    new(StringComparer.OrdinalIgnoreCase);

  private readonly TimeProvider _timeProvider;

  public DeviceClaimRegistry() : this(null) { }

  /// <summary>Creates the registry; the time provider is injectable so tests can pin claim times.</summary>
  public DeviceClaimRegistry(TimeProvider? timeProvider) {
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  /// <inheritdoc />
  public bool TryClaim(string? deviceSerial, string queueId, string queueName) {
    ArgumentException.ThrowIfNullOrWhiteSpace(queueId);
    var key = Normalize(deviceSerial);
    // A queue with no bound serial has no device identity to reserve. Storing every such queue under
    // one shared empty key would make them block each other for no reason (research R5).
    if (key is null) return true;

    var claim = new DeviceClaim(key, queueId, queueName ?? string.Empty, _timeProvider.GetUtcNow());
    return _claims.TryAdd(key, claim);
  }

  /// <inheritdoc />
  public void Release(string? deviceSerial, string queueId) {
    var key = Normalize(deviceSerial);
    if (key is null || string.IsNullOrWhiteSpace(queueId)) return;
    if (!_claims.TryGetValue(key, out var existing)) return;
    // Only the holder may release: a late release from a run that has already ended must not drop the
    // claim a newer run has since taken on the same device.
    if (!string.Equals(existing.QueueId, queueId, StringComparison.Ordinal)) return;
    _claims.TryRemove(new System.Collections.Generic.KeyValuePair<string, DeviceClaim>(key, existing));
  }

  /// <inheritdoc />
  public bool TryGetHolder(string? deviceSerial, out DeviceClaim claim) {
    var key = Normalize(deviceSerial);
    if (key is not null && _claims.TryGetValue(key, out var found)) {
      claim = found;
      return true;
    }
    claim = null!;
    return false;
  }

  /// <summary>Trims the serial and maps a blank one to <c>null</c> ("no device identity").</summary>
  private static string? Normalize(string? deviceSerial) {
    if (string.IsNullOrWhiteSpace(deviceSerial)) return null;
    return deviceSerial.Trim();
  }
}
