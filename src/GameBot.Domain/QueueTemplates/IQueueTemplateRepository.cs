using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameBot.Domain.QueueTemplates {
  /// <summary>
  /// Persistence contract for queue templates (name + ordered sequence entries).
  /// Unlike queues, templates persist their entries. Storage path: data/queue-templates.
  /// </summary>
  public interface IQueueTemplateRepository {
    Task<QueueTemplate?> GetAsync(string id);
    Task<IReadOnlyList<QueueTemplate>> ListAsync();

    /// <summary>Case-insensitive lookup by name; drives uniqueness and overwrite-by-name.</summary>
    Task<QueueTemplate?> FindByNameAsync(string name);

    Task<QueueTemplate> CreateAsync(QueueTemplate item);
    Task<QueueTemplate> UpdateAsync(QueueTemplate item);
    Task<bool> DeleteAsync(string id);

    /// <summary>Gives the full path of the file for <paramref name="id"/>, or null when the store has no file or the id is not valid. It has no side effect.</summary>
    string? GetFilePath(string id) => null;
  }
}
