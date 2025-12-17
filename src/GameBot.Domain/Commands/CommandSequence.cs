using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using GameBot.Domain.Commands.Blocks;

namespace GameBot.Domain.Commands
{
    /// <summary>
    /// Basic model placeholder for a command sequence; will be expanded in US1.
    /// </summary>
    public class CommandSequence
    {
        private readonly List<SequenceStep> _steps = new List<SequenceStep>();
        private readonly List<object> _blocks = new List<object>();

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        [JsonIgnore]
        public IReadOnlyList<SequenceStep> Steps => _steps.AsReadOnly();
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }

        // Optional blocks (heterogeneous array: Step|Block), persisted as "blocks"
        [JsonIgnore]
        public IReadOnlyList<object> Blocks => _blocks.AsReadOnly();

        public void SetBlocks(IEnumerable<object> blocks)
        {
            _blocks.Clear();
            if (blocks == null) return;
            _blocks.AddRange(blocks);
        }

        public void SetSteps(IEnumerable<SequenceStep> steps)
        {
            _steps.Clear();
            if (steps == null) return;
            _steps.AddRange(steps);
        }

        [JsonInclude]
        [JsonPropertyName("steps")]
        public System.Collections.ObjectModel.Collection<SequenceStep> StepsWritable
        {
            get => new System.Collections.ObjectModel.Collection<SequenceStep>(_steps);
            private set
            {
                _steps.Clear();
                if (value != null) foreach (var s in value) _steps.Add(s);
            }
        }

        [JsonInclude]
        [JsonPropertyName("blocks")]
        public System.Collections.ObjectModel.Collection<object> BlocksWritable
        {
            get => new System.Collections.ObjectModel.Collection<object>(_blocks);
            private set
            {
                _blocks.Clear();
                if (value != null) foreach (var b in value) _blocks.Add(b);
            }
        }
    }
}
