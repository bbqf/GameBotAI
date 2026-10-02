namespace GameBot.Service.Services.Notifications;

/// <summary>
/// One message that the worker started to send to a target (feature 126). Two records are equal when
/// all four fields match. The text compare is ordinal.
/// </summary>
/// <param name="QueueId">The queue of the message. For an alert, the queue of the alert.</param>
/// <param name="SequenceKey">The sequence ID. For an alert, the fixed key <c>alert</c>.</param>
/// <param name="Status">The status key: failure, success, recovered, cancelled, or alert.</param>
/// <param name="Text">The final message text.</param>
internal sealed record LastSentMessage(string QueueId, string SequenceKey, string Status, string Text);

/// <summary>
/// The last message that the worker started to send to each target, in memory (feature 126). The worker
/// drops a failure message for a target only when this record for the target is the same message.
/// <para>
/// Only the worker uses this class, one item at a time, so it has no lock. To prove this, each method
/// enters a guard. A second thread that enters while another thread is inside gets an exception.
/// </para>
/// </summary>
internal sealed class NotificationLastSentState {
  private readonly Dictionary<string, LastSentMessage> _last = new(StringComparer.Ordinal);
  private int _inside;

  /// <summary>True only when the target has a record and the record equals the message.</summary>
  public bool IsSame(string targetId, LastSentMessage message) {
    using (Enter()) return _last.TryGetValue(targetId, out var last) && last == message;
  }

  /// <summary>Replaces the record of the target.</summary>
  public void Record(string targetId, LastSentMessage message) {
    using (Enter()) _last[targetId] = message;
  }

  private Guard Enter() {
    if (Interlocked.Increment(ref _inside) != 1) {
      Interlocked.Decrement(ref _inside);
      throw new InvalidOperationException("The last-sent state has more than one user at the same time.");
    }

    return new Guard(this);
  }

  private readonly struct Guard : IDisposable {
    private readonly NotificationLastSentState _owner;

    public Guard(NotificationLastSentState owner) => _owner = owner;

    public void Dispose() => Interlocked.Decrement(ref _owner._inside);
  }
}
