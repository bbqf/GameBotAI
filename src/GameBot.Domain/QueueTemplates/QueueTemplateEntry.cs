namespace GameBot.Domain.QueueTemplates {
  /// <summary>
  /// A single, positional reference to a sequence within a <see cref="QueueTemplate"/>.
  /// Only the sequence reference is persisted; the resolved display name and stale flag
  /// are computed at read time. The same SequenceId may appear more than once in a template.
  /// </summary>
  public class QueueTemplateEntry {
    public string SequenceId { get; set; } = string.Empty;
  }
}
