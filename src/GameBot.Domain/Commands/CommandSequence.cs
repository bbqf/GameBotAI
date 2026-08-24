using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Parameters;

namespace GameBot.Domain.Commands {
  /// <summary>
  /// Basic model placeholder for a command sequence; will be expanded in US1.
  /// </summary>
  public class CommandSequence {
    private readonly System.Collections.ObjectModel.Collection<ParameterDeclaration> _parameters = new();
    private readonly List<SequenceStep> _steps = new List<SequenceStep>();
    private readonly List<object> _blocks = new List<object>();
    private readonly List<FlowStep> _flowSteps = new List<FlowStep>();
    private readonly List<BranchLink> _flowLinks = new List<BranchLink>();

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string EntryStepId { get; set; } = string.Empty;
    [JsonIgnore]
    public IReadOnlyList<SequenceStep> Steps => _steps.AsReadOnly();
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DelayRangeMs? InterStepDelayRangeMs { get; set; }

    /// <summary>
    /// Parameters this sequence accepts (feature 078). Empty means the sequence is unparametrized and
    /// behaves exactly as before the feature. A sequence need <em>not</em> declare a parameter merely
    /// to pass it through to a nested command: unbound names inherit from the enclosing run scope.
    /// </summary>
    [JsonIgnore]
    public System.Collections.ObjectModel.Collection<ParameterDeclaration> Parameters => _parameters;

    /// <summary>
    /// JSON projection of <see cref="Parameters"/>, omitted entirely when empty so an unparametrized
    /// sequence round-trips byte-identically to its pre-feature form.
    /// </summary>
    [JsonInclude]
    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public System.Collections.ObjectModel.Collection<ParameterDeclaration>? ParametersWritable {
      get => _parameters.Count == 0 ? null : _parameters;
      private set {
        _parameters.Clear();
        if (value is null) return;
        foreach (var declaration in value) _parameters.Add(declaration);
      }
    }

    // Optional blocks (heterogeneous array: Step|Block), persisted as "blocks"
    [JsonIgnore]
    public IReadOnlyList<object> Blocks => _blocks.AsReadOnly();
    [JsonIgnore]
    public IReadOnlyList<FlowStep> FlowSteps => _flowSteps.AsReadOnly();
    [JsonIgnore]
    public IReadOnlyList<BranchLink> FlowLinks => _flowLinks.AsReadOnly();

    public void SetBlocks(IEnumerable<object> blocks) {
      _blocks.Clear();
      if (blocks == null) return;
      _blocks.AddRange(blocks);
    }

    public void SetSteps(IEnumerable<SequenceStep> steps) {
      _steps.Clear();
      if (steps == null) return;
      _steps.AddRange(steps);
    }

    public void SetFlowSteps(IEnumerable<FlowStep> steps) {
      _flowSteps.Clear();
      if (steps == null) return;
      _flowSteps.AddRange(steps);
    }

    public void SetFlowLinks(IEnumerable<BranchLink> links) {
      _flowLinks.Clear();
      if (links == null) return;
      _flowLinks.AddRange(links);
    }

    public void SetFlowGraph(SequenceFlowGraph? graph) {
      if (graph == null) {
        EntryStepId = string.Empty;
        SetFlowSteps(Array.Empty<FlowStep>());
        SetFlowLinks(Array.Empty<BranchLink>());
        return;
      }

      Name = string.IsNullOrWhiteSpace(graph.Name) ? Name : graph.Name;
      Version = graph.Version;
      EntryStepId = graph.EntryStepId;
      SetFlowSteps(graph.Steps);
      SetFlowLinks(graph.Links);
    }

    [JsonInclude]
    [JsonPropertyName("steps")]
    public System.Collections.ObjectModel.Collection<SequenceStep> StepsWritable {
      get => new System.Collections.ObjectModel.Collection<SequenceStep>(_steps);
      private set {
        _steps.Clear();
        if (value != null) foreach (var s in value) _steps.Add(s);
      }
    }

    [JsonInclude]
    [JsonPropertyName("blocks")]
    public System.Collections.ObjectModel.Collection<object> BlocksWritable {
      get => new System.Collections.ObjectModel.Collection<object>(_blocks);
      private set {
        _blocks.Clear();
        if (value != null) foreach (var b in value) _blocks.Add(b);
      }
    }

    [JsonInclude]
    [JsonPropertyName("flowSteps")]
    public System.Collections.ObjectModel.Collection<FlowStep> FlowStepsWritable {
      get => new System.Collections.ObjectModel.Collection<FlowStep>(_flowSteps);
      private set {
        _flowSteps.Clear();
        if (value != null) foreach (var s in value) _flowSteps.Add(s);
      }
    }

    [JsonInclude]
    [JsonPropertyName("flowLinks")]
    public System.Collections.ObjectModel.Collection<BranchLink> FlowLinksWritable {
      get => new System.Collections.ObjectModel.Collection<BranchLink>(_flowLinks);
      private set {
        _flowLinks.Clear();
        if (value != null) foreach (var link in value) _flowLinks.Add(link);
      }
    }
  }
}
