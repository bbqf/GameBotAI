namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request body for duplicating a queue. The new <see cref="Name"/> MUST differ from the source
  /// queue's current name. The emulator fields are pre-filled from the source by the caller but,
  /// unlike the name, MAY be resubmitted unchanged — duplication is the only way to point a copy of
  /// an existing queue at a different emulator/instance.
  /// </summary>
  internal sealed class DuplicateQueueRequest {
    public string? Name { get; set; }
    public string? EmulatorSerial { get; set; }
    public string? EmulatorInstanceName { get; set; }
    public int? EmulatorInstanceIndex { get; set; }
  }
}
