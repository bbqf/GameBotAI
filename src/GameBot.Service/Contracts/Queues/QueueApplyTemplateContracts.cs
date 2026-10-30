using System.Collections.ObjectModel;

namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// One difference between the entries of a running queue and the entries of its linked template (feature 171).
  /// </summary>
  internal sealed class TemplateDifferenceResponse {
    /// <summary>
    /// Stable key of the difference, <c>&lt;type&gt;:&lt;entryId&gt;</c> (<c>order:all</c> for a new order). Send it back in
    /// <c>selectedKeys</c> to apply the difference. It is valid for the <c>baseToken</c> of the same response.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// <c>added</c>, <c>removed</c>, <c>enabled</c>, <c>disabled</c>, <c>parameters</c> (values or sequence of the entry),
    /// <c>schedule</c>, or <c>order</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The ID of the entry that the difference is about.</summary>
    public string EntryId { get; set; } = string.Empty;

    /// <summary>The display name of the sequence of the entry.</summary>
    public string SequenceName { get; set; } = string.Empty;

    /// <summary>One sentence that says what the apply does for this difference.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// True when a client should select the difference at first. A <c>schedule</c> difference is not selected at first:
    /// a schedule changes only on a direct instruction.
    /// </summary>
    public bool SelectedByDefault { get; set; }

    /// <summary>True when the difference starts or moves the schedule of the entry (an added or enabled entry, or a schedule change).</summary>
    public bool AffectsSchedule { get; set; }
  }

  /// <summary>Result of <c>GET /api/queues/{id}/template-diff</c> (feature 171).</summary>
  internal sealed class QueueTemplateDiffResponse {
    /// <summary>The queue.</summary>
    public string QueueId { get; set; } = string.Empty;

    /// <summary>The linked template.</summary>
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>
    /// True when the template, or the run, has entries with no entry ID. The list is empty then. Upgrade the template with
    /// <c>POST /api/queue-templates/{id}/upgrade</c>.
    /// </summary>
    public bool NeedsUpgrade { get; set; }

    /// <summary>Token of the template content. Send it back with the apply call. A change of the template changes the token.</summary>
    public string BaseToken { get; set; } = string.Empty;

    /// <summary>The differences, in template order, then the removals.</summary>
    public Collection<TemplateDifferenceResponse> Differences { get; } = new();
  }

  /// <summary>Body of <c>POST /api/queues/{id}/apply-template</c> (feature 171).</summary>
  internal sealed class ApplyTemplateRequest {
    /// <summary>The <c>baseToken</c> of the differences that the operator reviewed. Required.</summary>
    public string? BaseToken { get; set; }

    /// <summary>The keys of the differences to apply. An empty list applies nothing.</summary>
    public string[]? SelectedKeys { get; set; }
  }

  /// <summary>Result of a successful apply (feature 171).</summary>
  internal sealed class ApplyTemplateResponse {
    /// <summary>The keys that the apply put in the queue.</summary>
    public Collection<string> Applied { get; } = new();

    /// <summary>The version of the new run plan.</summary>
    public int PlanVersion { get; set; }
  }
}
