using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameBot.Domain.Commands {
  /// <summary>
  /// Repository contract for persisting and retrieving Command Sequences.
  /// Storage path: data/commands/sequences
  /// </summary>
  public interface ISequenceRepository {
    Task<CommandSequence?> GetAsync(string id);
    Task<IReadOnlyList<CommandSequence>> ListAsync();
    Task<CommandSequence> CreateAsync(CommandSequence sequence);
    Task<CommandSequence> UpdateAsync(CommandSequence sequence);
    Task<bool> DeleteAsync(string id);

    /// <summary>Gives the full path of the file for <paramref name="id"/>, or null when the store has no file or the id is not valid. It has no side effect.</summary>
    string? GetFilePath(string id) => null;
  }
}
