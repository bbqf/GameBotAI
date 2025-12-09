using System;
using System.Collections.Generic;

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
        public IReadOnlyList<SequenceStep> Steps => _steps.AsReadOnly();
        public DateTimeOffset? CreatedAt { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }

        public void SetSteps(IEnumerable<SequenceStep> steps)
        {
            _steps.Clear();
            if (steps == null) return;
            _steps.AddRange(steps);
        }
    }
}
