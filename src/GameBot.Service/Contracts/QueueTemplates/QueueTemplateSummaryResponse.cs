using System;

namespace GameBot.Service.Contracts.QueueTemplates {
  /// <summary>List representation of a queue template (no entries — used by the picker).</summary>
  internal class QueueTemplateSummaryResponse {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int EntryCount { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
  }
}
