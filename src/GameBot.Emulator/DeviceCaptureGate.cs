using System;
using System.Collections.Concurrent;

namespace GameBot.Emulator;

/// <summary>The capture state of one device (feature 121, data-model section 6).</summary>
public enum DeviceCaptureState {
  /// <summary>No unfinished capture. The next capture is allowed.</summary>
  Idle,

  /// <summary>One capture runs on the host. The next capture is refused.</summary>
  InFlight,

  /// <summary>
  /// The last capture timed out. The <c>screencap</c> process of the device can still run. The next
  /// capture is refused until the process ends or the device is repaired.
  /// </summary>
  Suspect
}

/// <summary>
/// Keeps at most one unfinished capture for each device (feature 121, FR-013, FR-014, research R-012).
/// The key is the device serial, compared without case. A timed-out capture puts the device in state
/// <see cref="DeviceCaptureState.Suspect"/>, so the service starts no new <c>screencap</c> process on a
/// blind device. All members are thread-safe.
/// </summary>
public sealed class DeviceCaptureGate {
  private sealed class Entry {
    public DeviceCaptureState State;
    public DateTimeOffset? SuspectSince;
    public DateTimeOffset? LastCheckAt;
  }

  private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
  private readonly TimeProvider _time;

  /// <summary>Creates a gate. <paramref name="time"/> is the clock for the check times.</summary>
  public DeviceCaptureGate(TimeProvider? time = null) {
    _time = time ?? TimeProvider.System;
  }

  /// <summary>
  /// Asks to start one capture. Returns true and sets the state to <see cref="DeviceCaptureState.InFlight"/>
  /// only when the state is <see cref="DeviceCaptureState.Idle"/>. Returns false in each other state.
  /// </summary>
  public bool TryBegin(string serial) {
    var entry = Get(serial);
    lock (entry) {
      if (entry.State != DeviceCaptureState.Idle) return false;
      entry.State = DeviceCaptureState.InFlight;
      return true;
    }
  }

  /// <summary>The capture ended with a frame. The state becomes Idle when a capture was in flight.</summary>
  public void Completed(string serial) => EndInFlight(serial);

  /// <summary>The capture ended with an error that is not a time-out. The state becomes Idle when a capture was in flight.</summary>
  public void Failed(string serial) => EndInFlight(serial);

  /// <summary>The capture timed out. The state becomes Suspect when a capture was in flight.</summary>
  public void TimedOut(string serial) {
    var entry = Get(serial);
    lock (entry) {
      if (entry.State != DeviceCaptureState.InFlight) return;
      entry.State = DeviceCaptureState.Suspect;
      entry.SuspectSince = _time.GetUtcNow();
      entry.LastCheckAt = entry.SuspectSince;
    }
  }

  /// <summary>
  /// Sets the state to Idle. Use it when a device check finds no running <c>screencap</c> process, and
  /// when a new capture loop starts for the serial (a repair).
  /// </summary>
  public void Clear(string serial) {
    var entry = Get(serial);
    lock (entry) {
      entry.State = DeviceCaptureState.Idle;
      entry.SuspectSince = null;
      entry.LastCheckAt = null;
    }
  }

  /// <summary>The state of the device. Idle for a serial that the gate does not know.</summary>
  public DeviceCaptureState GetState(string serial) {
    if (string.IsNullOrWhiteSpace(serial) || !_entries.TryGetValue(serial, out var entry)) return DeviceCaptureState.Idle;
    lock (entry) {
      return entry.State;
    }
  }

  /// <summary>True when the state of the device is <see cref="DeviceCaptureState.Suspect"/>.</summary>
  public bool IsSuspect(string serial) => GetState(serial) == DeviceCaptureState.Suspect;

  /// <summary>When the device became Suspect. Null when it is not Suspect.</summary>
  public DateTimeOffset? SuspectSince(string serial) {
    if (string.IsNullOrWhiteSpace(serial) || !_entries.TryGetValue(serial, out var entry)) return null;
    lock (entry) {
      return entry.State == DeviceCaptureState.Suspect ? entry.SuspectSince : null;
    }
  }

  /// <summary>When the last device check of a Suspect device ran. Null when it is not Suspect.</summary>
  public DateTimeOffset? LastCheckAt(string serial) {
    if (string.IsNullOrWhiteSpace(serial) || !_entries.TryGetValue(serial, out var entry)) return null;
    lock (entry) {
      return entry.State == DeviceCaptureState.Suspect ? entry.LastCheckAt : null;
    }
  }

  /// <summary>Records the time of a device check for a Suspect device.</summary>
  public void RecordCheck(string serial) {
    var entry = Get(serial);
    lock (entry) {
      if (entry.State == DeviceCaptureState.Suspect) entry.LastCheckAt = _time.GetUtcNow();
    }
  }

  private void EndInFlight(string serial) {
    var entry = Get(serial);
    lock (entry) {
      // A Suspect state stays: only a device check or a repair clears it.
      if (entry.State == DeviceCaptureState.InFlight) entry.State = DeviceCaptureState.Idle;
    }
  }

  private Entry Get(string serial) {
    ArgumentException.ThrowIfNullOrWhiteSpace(serial);
    return _entries.GetOrAdd(serial, _ => new Entry());
  }
}
