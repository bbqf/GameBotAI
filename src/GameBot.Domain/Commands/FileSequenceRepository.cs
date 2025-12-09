using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameBot.Domain.Commands
{
    /// <summary>
    /// Minimal file-backed repository stub targeting data/commands/sequences.
    /// Actual serialization and validation to be completed in US1.
    /// </summary>
    public class FileSequenceRepository : ISequenceRepository
    {
        private readonly string _root;
        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public FileSequenceRepository(string dataRoot)
        {
            _root = Path.Combine(dataRoot, "commands", "sequences");
            Directory.CreateDirectory(_root);
        }

        public async Task<CommandSequence?> GetAsync(string id)
        {
            var path = Path.Combine(_root, id + ".json");
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var result = await JsonSerializer.DeserializeAsync<CommandSequence>(stream, _jsonOptions).ConfigureAwait(false);
            return result;
        }

        public async Task<IReadOnlyList<CommandSequence>> ListAsync()
        {
            var list = new List<CommandSequence>();
            foreach (var file in Directory.EnumerateFiles(_root, "*.json"))
            {
                using var stream = File.OpenRead(file);
                var seq = await JsonSerializer.DeserializeAsync<CommandSequence>(stream, _jsonOptions).ConfigureAwait(false);
                if (seq != null) list.Add(seq);
            }
            return list;
        }

        public async Task<CommandSequence> CreateAsync(CommandSequence sequence)
        {
            ArgumentNullException.ThrowIfNull(sequence);
            if (string.IsNullOrWhiteSpace(sequence.Id))
            {
                sequence.Id = Guid.NewGuid().ToString("N");
            }
            var path = Path.Combine(_root, sequence.Id + ".json");
            using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, sequence, _jsonOptions).ConfigureAwait(false);
            return sequence;
        }

        public async Task<CommandSequence> UpdateAsync(CommandSequence sequence)
        {
            ArgumentNullException.ThrowIfNull(sequence);
            if (string.IsNullOrWhiteSpace(sequence.Id))
            {
                throw new InvalidOperationException("Sequence Id is required for update");
            }
            var path = Path.Combine(_root, sequence.Id + ".json");
            using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, sequence, _jsonOptions).ConfigureAwait(false);
            return sequence;
        }

        public Task<bool> DeleteAsync(string id)
        {
            var path = Path.Combine(_root, id + ".json");
            if (!File.Exists(path)) return Task.FromResult(false);
            File.Delete(path);
            return Task.FromResult(true);
        }
    }
}
