using System.Collections.ObjectModel;

namespace GameBot.Service.Contracts.QueueTemplates {
  /// <summary>Single-template representation including its ordered, resolved sequence entries.</summary>
  internal sealed class QueueTemplateDetailResponse : QueueTemplateSummaryResponse {
    public Collection<QueueTemplateEntryResponse> Entries { get; } = new Collection<QueueTemplateEntryResponse>();
  }

  /// <summary>
  /// A template entry projected for responses. <see cref="SequenceName"/> is resolved from the
  /// sequence store; <see cref="Stale"/> is true when the referenced sequence no longer exists.
  /// </summary>
  internal sealed class QueueTemplateEntryResponse {
    public string SequenceId { get; set; } = string.Empty;
    public string? SequenceName { get; set; }
    public bool Stale { get; set; }
  }
}
