using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameBot.Domain.Commands
{
    /// <summary>
    /// Basic model placeholder for a command sequence; will be expanded in US1.
    /// </summary>
    public class CommandSequence
    {
        private readonly List<SequenceStep> _steps = new List<SequenceStep>();

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        [JsonIgnore]
        public IReadOnlyList<SequenceStep> Steps => _steps.AsReadOnly();
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }

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
    }
}
