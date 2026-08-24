using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using GameBot.Domain.Parameters;

namespace GameBot.Domain.Commands;

public sealed class Command {
  private readonly Collection<ParameterDeclaration> _parameters = new();

  public required string Id { get; set; }
  public required string Name { get; set; }
  public string? TriggerId { get; set; }
  public Collection<CommandStep> Steps { get; init; } = new();
  // Optional detection target enabling image-based coordinate resolution for action steps
  public DetectionTarget? Detection { get; set; }

  /// <summary>
  /// Parameters this command accepts (feature 078). Empty means the command is unparametrized and
  /// behaves exactly as it did before the feature; commands stored before it omit the member in JSON
  /// and therefore deserialize as empty.
  /// </summary>
  [JsonIgnore]
  public Collection<ParameterDeclaration> Parameters => _parameters;

  /// <summary>
  /// JSON projection of <see cref="Parameters"/>. Returns <c>null</c> when there are no declarations
  /// so the member is omitted entirely, letting an unparametrized command round-trip byte-identically
  /// to its pre-feature form instead of gaining a <c>"parameters": []</c> line.
  /// </summary>
  [JsonInclude]
  [JsonPropertyName("parameters")]
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public Collection<ParameterDeclaration>? ParametersWritable {
    get => _parameters.Count == 0 ? null : _parameters;
    private set {
      _parameters.Clear();
      if (value is null) return;
      foreach (var declaration in value) _parameters.Add(declaration);
    }
  }
}
