using System;

namespace GameBot.Service.Contracts.QueueTemplates {
  /// <summary>List representation of a queue template (no entries — used by the picker).</summary>
  internal class QueueTemplateSummaryResponse {
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int EntryCount { get; set; }

    /// <summary>
    /// True when at least one entry has no entry ID (feature 171). Such a template is from an older
    /// release. It works as before, but a live apply to a running queue needs the manual upgrade first
    /// (<c>POST /api/queue-templates/{id}/upgrade</c>).
    /// </summary>
    public bool NeedsUpgrade { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
  }
}
