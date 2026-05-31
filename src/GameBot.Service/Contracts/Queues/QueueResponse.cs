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
  }
}
