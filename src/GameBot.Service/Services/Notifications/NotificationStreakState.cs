namespace GameBot.Service.Services.Notifications;

/// <summary>
/// The open failure streaks, in memory (feature 120, FR-023). One key is a queue ID and a sequence ID.
/// A key in the set is an open streak. An absent key is a closed streak.
/// <para>
/// Only the worker uses this class, one item at a time, so it has no lock. To prove this, each method
/// enters a guard. A second thread that enters while another thread is inside gets an exception.
/// </para>
/// </summary>
internal sealed class NotificationStreakState {
  private readonly HashSet<(string QueueId, string SequenceId)> _open = new();
  private int _inside;

  public bool IsOpen(string queueId, string sequenceId) {
    using (Enter()) return _open.Contains((queueId, sequenceId));
  }

  public void Open(string queueId, string sequenceId) {
    using (Enter()) _open.Add((queueId, sequenceId));
  }

  public void Close(string queueId, string sequenceId) {
    using (Enter()) _open.Remove((queueId, sequenceId));
  }

  /// <summary>Removes all keys of a queue. Returns the count of removed keys.</summary>
  public int RemoveQueue(string queueId) {
    using (Enter()) return _open.RemoveWhere(k => string.Equals(k.QueueId, queueId, StringComparison.Ordinal));
  }

  /// <summary>The count of open streaks of a queue.</summary>
  public int OpenCount(string queueId) {
    using (Enter()) return _open.Count(k => string.Equals(k.QueueId, queueId, StringComparison.Ordinal));
  }

  /// <summary>The count of all open streaks.</summary>
  public int TotalOpenCount {
    get { using (Enter()) return _open.Count; }
  }

  private Guard Enter() {
    if (Interlocked.Increment(ref _inside) != 1) {
      Interlocked.Decrement(ref _inside);
      throw new InvalidOperationException("The streak state has more than one user at the same time.");
    }

    return new Guard(this);
  }

  private readonly struct Guard : IDisposable {
    private readonly NotificationStreakState _owner;

    public Guard(NotificationStreakState owner) => _owner = owner;

    public void Dispose() => Interlocked.Decrement(ref _owner._inside);
  }
}
