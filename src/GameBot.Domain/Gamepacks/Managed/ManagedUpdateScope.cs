using GameBot.Domain.QueueTemplates;

namespace GameBot.Domain.Gamepacks.Managed;

/// <summary>
/// The scope of a managed call (feature 148, research R4): the templates and queues that the gamepack affects. A template is
/// affected when one entry refers to a sequence of the installed version. A queue is affected when it links to an affected
/// template. The optional template list limits both. This class has no side effect.
/// </summary>
public sealed class ManagedUpdateScope {
  private ManagedUpdateScope() {
  }

  /// <summary>The affected templates, limited by the template list.</summary>
  public IReadOnlyList<QueueTemplate> AffectedTemplates { get; private init; } = Array.Empty<QueueTemplate>();

  /// <summary>The queues that link to an affected template.</summary>
  public IReadOnlyList<ManagedQueueInfo> AffectedQueues { get; private init; } = Array.Empty<ManagedQueueInfo>();

  /// <summary>The affected queues that run now. The flow stops these.</summary>
  public IReadOnlyList<ManagedQueueInfo> QueuesToStop => AffectedQueues.Where(q => q.IsRunning).ToList();

  /// <summary>The names in the template list that exist but that the gamepack does not affect.</summary>
  public IReadOnlyList<string> NotAffectedTemplates { get; private init; } = Array.Empty<string>();

  /// <summary>The names in the template list that match no template.</summary>
  public IReadOnlyList<string> MissingTemplates { get; private init; } = Array.Empty<string>();

  /// <summary>Finds the scope.</summary>
  /// <param name="templates">All templates.</param>
  /// <param name="queues">All queues.</param>
  /// <param name="installedSequenceIds">The ids of the sequences of the installed version. Empty for a first install.</param>
  /// <param name="requestedTemplates">The template list of the request, or null for all templates.</param>
  public static ManagedUpdateScope Compute(
      IReadOnlyList<QueueTemplate> templates,
      IReadOnlyList<ManagedQueueInfo> queues,
      IReadOnlyCollection<string> installedSequenceIds,
      IReadOnlyList<string>? requestedTemplates) {
    ArgumentNullException.ThrowIfNull(templates);
    ArgumentNullException.ThrowIfNull(queues);
    ArgumentNullException.ThrowIfNull(installedSequenceIds);

    var ids = new HashSet<string>(installedSequenceIds, StringComparer.OrdinalIgnoreCase);
    bool IsAffected(QueueTemplate t) => t.Entries.Any(e => ids.Contains(e.SequenceId));

    var requested = (requestedTemplates ?? Array.Empty<string>())
      .Where(n => !string.IsNullOrWhiteSpace(n))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToList();

    var missing = new List<string>();
    var notAffected = new List<string>();
    List<QueueTemplate> affected;
    if (requested.Count == 0) {
      affected = templates.Where(IsAffected).ToList();
    }
    else {
      affected = new List<QueueTemplate>();
      foreach (var name in requested) {
        var found = templates.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (found is null) {
          missing.Add(name);
        }
        else if (IsAffected(found)) {
          affected.Add(found);
        }
        else {
          notAffected.Add(found.Name);
        }
      }
    }

    var affectedIds = new HashSet<string>(affected.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
    var affectedQueues = queues
      .Where(q => !string.IsNullOrWhiteSpace(q.LinkedTemplateId) && affectedIds.Contains(q.LinkedTemplateId))
      .ToList();

    return new ManagedUpdateScope {
      AffectedTemplates = affected,
      AffectedQueues = affectedQueues,
      NotAffectedTemplates = notAffected,
      MissingTemplates = missing
    };
  }
}
