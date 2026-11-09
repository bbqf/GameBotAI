namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request body for <c>POST /api/queues/{id}/run-next</c> (feature 184): queue one run of one template
  /// entry. The entry runs once, right after the sequence that runs now.
  /// </summary>
  internal sealed class RunNextRequest {
    /// <summary>The stable ID of an enabled entry of the running plan. Must be non-blank.</summary>
    public string? EntryId { get; set; }
  }

  /// <summary>Response body of a successful run-next call.</summary>
  internal sealed class RunNextResponse {
    /// <summary>The ID of the entry.</summary>
    public string EntryId { get; set; } = string.Empty;

    /// <summary>The sequence of the entry.</summary>
    public string SequenceId { get; set; } = string.Empty;

    /// <summary>True when the entry already waited in the register. The call then added nothing.</summary>
    public bool AlreadyQueued { get; set; }

    /// <summary>The state of the entry after the call. It is always <c>queued</c>.</summary>
    public string State { get; set; } = "queued";
  }
}
