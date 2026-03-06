using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using GameBot.Domain.Actions;

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
        private static readonly Regex SafeIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

        public FileSequenceRepository(string dataRoot)
        {
            _root = Path.Combine(dataRoot, "commands", "sequences");
            Directory.CreateDirectory(_root);
        }

        private bool TryGetSafePath(string id, [NotNullWhen(true)] out string? path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (!SafeIdPattern.IsMatch(id)) return false;

            var baseDirFull = Path.GetFullPath(_root);
            var candidate = Path.Combine(baseDirFull, id + ".json");
            var candidateFull = Path.GetFullPath(candidate);

            if (!candidateFull.StartsWith(baseDirFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !string.Equals(candidateFull, baseDirFull, StringComparison.Ordinal))
            {
                return false;
            }

            path = candidateFull;
            return true;
        }

        public async Task<CommandSequence?> GetAsync(string id)
        {
            if (!TryGetSafePath(id, out var path)) return null;
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
            ValidateActionPayloads(sequence);
            Directory.CreateDirectory(_root);
            if (string.IsNullOrWhiteSpace(sequence.Id))
            {
                sequence.Id = Guid.NewGuid().ToString("N");
            }
            if (!TryGetSafePath(sequence.Id, out var path))
            {
                throw new InvalidOperationException("Generated or provided sequence ID is invalid for file storage.");
            }
            using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, sequence, _jsonOptions).ConfigureAwait(false);
            return sequence;
        }

        public async Task<CommandSequence> UpdateAsync(CommandSequence sequence)
        {
            ArgumentNullException.ThrowIfNull(sequence);
            ValidateActionPayloads(sequence);
            Directory.CreateDirectory(_root);
            if (string.IsNullOrWhiteSpace(sequence.Id))
            {
                throw new InvalidOperationException("Sequence Id is required for update");
            }
            if (!TryGetSafePath(sequence.Id, out var path))
            {
                throw new InvalidOperationException("Invalid sequence identifier");
            }
            using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, sequence, _jsonOptions).ConfigureAwait(false);
            return sequence;
        }

        public Task<bool> DeleteAsync(string id)
        {
            if (!TryGetSafePath(id, out var path)) return Task.FromResult(false);
            if (!File.Exists(path)) return Task.FromResult(false);
            File.Delete(path);
            return Task.FromResult(true);
        }

        private static void ValidateActionPayloads(CommandSequence sequence)
        {
            var supportedActionTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ActionTypes.Tap,
                ActionTypes.Swipe,
                ActionTypes.Key,
                ActionTypes.ConnectToGame
            };

            foreach (var step in sequence.FlowSteps)
            {
                if (step.StepType != FlowStepType.Action)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(step.PayloadRef))
                {
                    throw new InvalidOperationException($"Action step '{step.StepId}' requires payloadRef.");
                }

                if (!step.PayloadRef.Contains(':', StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = step.PayloadRef.IndexOf(':', StringComparison.Ordinal);
                if (separatorIndex <= 0)
                {
                    throw new InvalidOperationException($"Action step '{step.StepId}' has malformed action payload reference.");
                }

                var actionType = step.PayloadRef[..separatorIndex].Trim();
                if (!supportedActionTypes.Contains(actionType))
                {
                    throw new InvalidOperationException($"Action step '{step.StepId}' references unsupported action type '{actionType}'.");
                }
            }
        }
    }
}
