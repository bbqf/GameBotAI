using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Maps parameter declarations, bindings, scope entries and validation feedback between the domain
/// and the wire (feature 078). Shared by the commands, sequences and queue-template endpoints so the
/// three cannot drift in how they represent the same concepts.
/// </summary>
internal static class ParameterDtoMapper {
  /// <summary>Converts declaration DTOs to domain declarations. Null input yields an empty list.</summary>
  /// <param name="dtos">Wire declarations.</param>
  public static List<ParameterDeclaration> ToDomainDeclarations(IEnumerable<ParameterDeclarationDto>? dtos) {
    var result = new List<ParameterDeclaration>();
    if (dtos is null) return result;
    foreach (var dto in dtos) {
      result.Add(new ParameterDeclaration {
        Name = dto.Name ?? string.Empty,
        Type = string.Equals(dto.Type, "number", System.StringComparison.OrdinalIgnoreCase)
            ? ParameterValueType.Number
            : ParameterValueType.Text,
        Default = dto.Default,
        Required = dto.Required ?? false,
        Description = dto.Description
      });
    }

    return result;
  }

  /// <summary>Projects declarations for a response; returns null when there are none, so the member is omitted.</summary>
  /// <param name="declarations">Domain declarations.</param>
  public static Collection<ParameterDeclarationDto>? ToResponseDeclarations(
      IReadOnlyCollection<ParameterDeclaration>? declarations) {
    if (declarations is null || declarations.Count == 0) return null;
    return new Collection<ParameterDeclarationDto>(declarations.Select(d => new ParameterDeclarationDto {
      Name = d.Name,
      Type = d.Type == ParameterValueType.Number ? "number" : "text",
      Default = d.Default,
      Required = d.Required,
      Description = d.Description
    }).ToList());
  }

  /// <summary>Converts binding DTOs to domain bindings. Null input yields an empty list.</summary>
  /// <param name="dtos">Wire bindings.</param>
  public static List<ParameterBinding> ToDomainBindings(IEnumerable<ParameterBindingDto>? dtos) {
    var result = new List<ParameterBinding>();
    if (dtos is null) return result;
    foreach (var dto in dtos) {
      if (string.IsNullOrWhiteSpace(dto.Name)) continue;
      result.Add(new ParameterBinding { Name = dto.Name!, Value = dto.Value });
    }

    return result;
  }

  /// <summary>Projects bindings for a response; null when there are none.</summary>
  /// <param name="bindings">Domain bindings.</param>
  public static Collection<ParameterBindingDto>? ToResponseBindings(
      IReadOnlyCollection<ParameterBinding>? bindings) {
    if (bindings is null || bindings.Count == 0) return null;
    return new Collection<ParameterBindingDto>(
        bindings.Select(b => new ParameterBindingDto { Name = b.Name, Value = b.Value }).ToList());
  }

  /// <summary>Projects a scope description for the authoring UI's picker and preview.</summary>
  /// <param name="entries">Scope entries.</param>
  public static Collection<ParameterScopeEntryDto> ToResponseScopeEntries(IEnumerable<ScopeEntry> entries) =>
      new(entries.Select(e => new ParameterScopeEntryDto {
        Name = e.Name,
        Value = e.Value,
        OriginLayer = e.OriginLayer,
        Declared = e.Declared,
        Description = e.Description
      }).ToList());

  /// <summary>
  /// The names an editor may offer for a command or sequence: its own declarations plus the reserved
  /// queue built-ins. Values are null because no run is in progress; the origin layer still tells the
  /// operator where each would come from.
  /// </summary>
  /// <param name="declarations">The entity's own declarations.</param>
  public static Collection<ParameterScopeEntryDto> BuildEditorScope(
      IReadOnlyCollection<ParameterDeclaration> declarations) {
    var entries = declarations.Select(d => new ParameterScopeEntryDto {
      Name = d.Name,
      Value = d.Default,
      OriginLayer = d.Default is null ? ParameterScopeLayers.Entry : ParameterScopeLayers.Default,
      Declared = true,
      Description = d.Description
    }).ToList();

    entries.AddRange(ParameterNameRules.BuiltIns.Select(b => new ParameterScopeEntryDto {
      Name = b.Name,
      Value = null,
      OriginLayer = ParameterScopeLayers.Queue,
      Declared = false,
      Description = b.Description
    }));

    return new Collection<ParameterScopeEntryDto>(entries);
  }

  /// <summary>Projects validation warnings for a response; null when there are none.</summary>
  /// <param name="warnings">Warnings to project.</param>
  public static Collection<ParameterWarningDto>? ToResponseWarnings(
      IReadOnlyCollection<ParameterValidationIssue>? warnings) {
    if (warnings is null || warnings.Count == 0) return null;
    return new Collection<ParameterWarningDto>(warnings.Select(w => new ParameterWarningDto {
      Code = w.Code,
      Message = w.Message,
      FieldPath = w.FieldPath,
      ParameterName = w.ParameterName,
      EntryIndex = w.EntryIndex
    }).ToList());
  }

  /// <summary>
  /// Renders blocking validation errors as the standard error body: the first issue's code plus a
  /// details array so the UI can anchor each message at its offending field.
  /// </summary>
  /// <param name="errors">Blocking issues; must be non-empty.</param>
  public static object ToErrorBody(IReadOnlyList<ParameterValidationIssue> errors) => new {
    error = errors[0].Code,
    message = errors[0].Message,
    details = errors.Select(e => new {
      code = e.Code,
      message = e.Message,
      fieldPath = e.FieldPath,
      parameterName = e.ParameterName
    }).ToList()
  };
}
