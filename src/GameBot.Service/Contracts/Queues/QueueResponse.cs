using GameBot.Domain.Queues;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>List/summary representation of a queue (config + runtime status snapshot).</summary>
  internal class QueueResponse {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EmulatorSerial { get; set; } = string.Empty;
    public bool CycleExecution { get; set; }
    public QueueExecutionStatus Status { get; set; } = QueueExecutionStatus.Stopped;
    public int EntryCount { get; set; }

    /// <summary>Stable ID of the linked queue template, or null when the queue is unlinked.</summary>
    public string? LinkedTemplateId { get; set; }

    /// <summary>Stable ID of the linked game, or null when the queue is unlinked from a game.</summary>
    public string? LinkedGameId { get; set; }

    /// <summary>Display name of the linked game, resolved at response time; null when unlinked or unresolvable.</summary>
    public string? LinkedGameName { get; set; }
  }
}
