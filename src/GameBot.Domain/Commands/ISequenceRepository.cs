using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameBot.Domain.Commands
{
    /// <summary>
    /// Repository contract for persisting and retrieving Command Sequences.
    /// Storage path: data/commands/sequences
    /// </summary>
    public interface ISequenceRepository
    {
        Task<CommandSequence?> GetAsync(string id);
        Task<IReadOnlyList<CommandSequence>> ListAsync();
        Task<CommandSequence> CreateAsync(CommandSequence sequence);
        Task<CommandSequence> UpdateAsync(CommandSequence sequence);
        Task<bool> DeleteAsync(string id);
    }
}
