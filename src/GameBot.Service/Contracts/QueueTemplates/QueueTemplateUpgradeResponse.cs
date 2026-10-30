using System.Collections.ObjectModel;

namespace GameBot.Service.Contracts.QueueTemplates {
  /// <summary>One running entry that the upgrade matches to a template entry (feature 171).</summary>
  internal sealed class QueueTemplateUpgradeMatchResponse {
    /// <summary>The running queue.</summary>
    public string QueueId { get; set; } = string.Empty;

    /// <summary>The position of the entry in the template, from 0.</summary>
    public int Position { get; set; }

    /// <summary>The name of the sequence of the entry, or its ID when the sequence no longer exists.</summary>
    public string SequenceName { get; set; } = string.Empty;
  }

  /// <summary>One running entry that the upgrade cannot match to a template entry (feature 171).</summary>
  internal sealed class QueueTemplateUpgradeUnmatchedResponse {
    /// <summary>The running queue.</summary>
    public string QueueId { get; set; } = string.Empty;

    /// <summary>The position in the template, from 0.</summary>
    public int Position { get; set; }

    /// <summary>The sequence of the running entry, or null when the running queue has no entry at this position.</summary>
    public string? RunningSequence { get; set; }

    /// <summary>The sequence of the template entry, or null when the template has no entry at this position.</summary>
    public string? TemplateSequence { get; set; }

    /// <summary>Why the entry does not match.</summary>
    public string Reason { get; set; } = string.Empty;
  }

  /// <summary>
  /// The answer of <c>POST /api/queue-templates/{id}/upgrade?dryRun=true</c>: what the upgrade would do.
  /// </summary>
  internal sealed class QueueTemplateUpgradePreviewResponse {
    /// <summary>The number of template entries that have no entry ID now.</summary>
    public int EntriesToChange { get; set; }

    /// <summary>The entries of running queues that use the template, with their match.</summary>
    public Collection<QueueTemplateUpgradeMatchResponse> RunningMatch { get; } = new();

    /// <summary>The running entries that the upgrade cannot match. The real upgrade is refused while this list is not empty.</summary>
    public Collection<QueueTemplateUpgradeUnmatchedResponse> Unmatched { get; } = new();
  }

  /// <summary>The answer of the real upgrade.</summary>
  internal sealed class QueueTemplateUpgradeResultResponse {
    /// <summary>The number of template entries that got an entry ID. 0 when the template was already upgraded.</summary>
    public int EntriesChanged { get; set; }
  }
}
