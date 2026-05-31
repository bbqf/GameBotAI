using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameBot.Domain.Queues {
  /// <summary>
  /// Persistence contract for emulator execution queue <b>configuration</b> only
  /// (name, bound emulator serial, cycle flag). Storage path: data/queues.
  /// Sequence entries and execution status are not persisted (see <see cref="IQueueRuntimeStore"/>).
  /// </summary>
  public interface IQueueRepository {
    Task<ExecutionQueue?> GetAsync(string id);
    Task<IReadOnlyList<ExecutionQueue>> ListAsync();
    Task<ExecutionQueue> CreateAsync(ExecutionQueue queue);
    Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue);
    Task<bool> DeleteAsync(string id);
  }
}
