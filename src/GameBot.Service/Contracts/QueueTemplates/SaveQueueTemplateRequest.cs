namespace GameBot.Service.Contracts.QueueTemplates {
  /// <summary>
  /// Request body for saving (create or overwrite-by-name) a queue template.
  /// When a template with the same name (case-insensitive) already exists, the caller must set
  /// <see cref="Overwrite"/> to true to replace it; otherwise the server responds 409.
  /// </summary>
  internal sealed class SaveQueueTemplateRequest {
    public string? Name { get; set; }

    /// <summary>
    /// Ordered list of sequence entries to persist. Replaces the former <c>SequenceIds</c>
    /// string array; each entry carries its sequence reference and optional schedule type.
    /// </summary>
    public TemplateEntrySaveRequest[]? Entries { get; set; }

    public bool Overwrite { get; set; }
  }
}
