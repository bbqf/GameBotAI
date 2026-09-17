using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameBot.Domain.Queues {
  /// <summary>
  /// Durable record of which queues have a live run (feature 098, #203). A queue is recorded when its
  /// run starts and forgotten when the run ends while the service is not shutting down, so after a
  /// restart — graceful or not — the record names exactly the queues that were Running when the service
  /// went down. Read once at service start to resume the queues that opt in.
  /// </summary>
  public interface IQueueRunStateStore {
    /// <summary>Records <paramref name="queueId"/> as Running. Idempotent.</summary>
    /// <exception cref="System.IO.IOException">The record could not be written.</exception>
    Task MarkRunningAsync(string queueId);

    /// <summary>Forgets <paramref name="queueId"/>. A no-op when it is not recorded.</summary>
    /// <exception cref="System.IO.IOException">The record could not be written.</exception>
    Task ClearAsync(string queueId);

    /// <summary>The recorded queue ids; empty when nothing has been recorded.</summary>
    /// <exception cref="System.IO.InvalidDataException">The stored record is corrupt.</exception>
    Task<IReadOnlyList<string>> ListRunningAsync();
  }
}
