using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Services;
using GameBot.Service.Contracts.QueueTemplates;
using GameBot.Service.Services.QueueExecution;

namespace GameBot.Service.Endpoints;

internal static class QueueTemplatesEndpoints {
  public static IEndpointRouteBuilder MapQueueTemplateEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.QueueTemplates).WithTags("QueueTemplates");

    group.MapGet("", async (IQueueTemplateRepository repo) => {
      var list = await repo.ListAsync().ConfigureAwait(false);
      var resp = list.Select(BuildSummary).ToList();
      return Results.Ok(resp);
    }).WithName("ListQueueTemplates");

    group.MapGet("{id}", async (string id, IQueueTemplateRepository repo, ISequenceRepository sequences) => {
      var template = await repo.GetAsync(id).ConfigureAwait(false);
      if (template is null) return NotFound();
      return Results.Ok(await BuildDetailAsync(template, sequences).ConfigureAwait(false));
    }).WithName("GetQueueTemplate");

    group.MapPost("", async (
        SaveQueueTemplateRequest? req,
        IQueueTemplateRepository repo,
        ISequenceRepository sequences,
        ICommandRepository commands,
        IImageRepository images,
        CancellationToken ct) => {
      var name = req?.Name?.Trim();
      var nameError = ValidateName(name);
      if (nameError is not null) return Error(400, "invalid_request", nameError);

      // Validate each entry in the request
      var entries = req!.Entries ?? Array.Empty<TemplateEntrySaveRequest>();
      for (var i = 0; i < entries.Length; i++) {
        var entry = entries[i];
        if (string.IsNullOrWhiteSpace(entry.SequenceId))
          return Error(400, "invalid_request", $"entries[{i}].sequenceId is required and must be non-blank");

        if (!TryParseScheduleType(entry.ScheduleType, out var scheduleType))
          return Error(400, "invalid_request",
            $"entries[{i}].scheduleType '{entry.ScheduleType}' is not valid; accepted values: OncePerRun, EveryStep, Timer, AtQueueStart, BeforeEachRun");

        if (scheduleType == ScheduleType.Timer) {
          var hasTimeOfDay = !string.IsNullOrWhiteSpace(entry.TimerTimeOfDay);
          var hasRelative = !string.IsNullOrWhiteSpace(entry.TimerRelativeOffset);
          if (hasTimeOfDay == hasRelative)
            return Error(400, "invalid_request",
              $"entries[{i}] must set exactly one of timerTimeOfDay or timerRelativeOffset when scheduleType is Timer");
          if (hasTimeOfDay && !TimerTimeOfDayFormat.TryParse(entry.TimerTimeOfDay, out _))
            return Error(400, "invalid_request",
              $"entries[{i}].timerTimeOfDay '{entry.TimerTimeOfDay}' is not a valid time of day; accepted format: "
              + $"{TimerTimeOfDayFormat.AcceptedFormatText}, for example '15:30' or '15:30:00'");
          if (hasRelative && !RelativeOffsetParser.TryParse(entry.TimerRelativeOffset, out _, out var offsetError))
            return Error(400, "invalid_request",
              $"entries[{i}].timerRelativeOffset {offsetError}");
        }
      }

      var existing = await repo.FindByNameAsync(name!).ConfigureAwait(false);
      if (existing is not null && !req!.Overwrite) {
        return Error(409, "template_exists",
          $"A template named '{existing.Name}' already exists.",
          "Resend with overwrite=true to replace.");
      }

      var target = existing ?? new QueueTemplate();
      target.Name = name!;
      target.Entries.Clear();
      foreach (var entry in entries) {
        // Schedule type already validated above; parse is guaranteed to succeed here.
        var scheduleType = string.IsNullOrWhiteSpace(entry.ScheduleType)
          ? ScheduleType.OncePerRun
          : Enum.Parse<ScheduleType>(entry.ScheduleType, ignoreCase: true);
        // The value already passed validation above, so the parse succeeds here.
        TimeOnly? timerTime = scheduleType == ScheduleType.Timer
            && !string.IsNullOrWhiteSpace(entry.TimerTimeOfDay)
            && TimerTimeOfDayFormat.TryParse(entry.TimerTimeOfDay, out var parsedTime)
          ? parsedTime
          : null;
        TimeSpan? timerOffset = scheduleType == ScheduleType.Timer
            && !string.IsNullOrWhiteSpace(entry.TimerRelativeOffset)
            && RelativeOffsetParser.TryParse(entry.TimerRelativeOffset, out var parsedOffset, out _)
          ? parsedOffset
          : null;

        var domainEntry = new QueueTemplateEntry {
          SequenceId = entry.SequenceId!,
          ScheduleType = scheduleType,
          TimerTimeOfDay = timerTime,
          TimerRelativeOffset = timerOffset,
          // Null (omitted by older clients) means enabled; only an explicit false disables.
          Enabled = entry.Enabled ?? true
        };
        foreach (var binding in ParameterDtoMapper.ToDomainBindings(entry.ParameterValues)) {
          domainEntry.ParameterValues.Add(binding);
        }

        target.Entries.Add(domainEntry);
      }

      // Feature 078: only a malformed or reserved value name blocks the save (FR-012b's unused-value
      // feedback and FR-021's unsatisfied-required feedback are warnings, surfaced in the response).
      var nameErrors = target.Entries
        .SelectMany((e, index) => e.ParameterValues
          .Select(b => (Index: index, Name: b.Name, Error: ParameterNameRules.ValidateName(b.Name)))
          .Where(x => x.Error is not null))
        .Select(x => new ParameterValidationIssue(
          ParameterValidationCodes.InvalidValueName,
          $"Entry {x.Index}: {x.Error}.",
          null,
          x.Name,
          x.Index))
        .ToList();
      if (nameErrors.Count > 0) {
        return Results.BadRequest(ParameterDtoMapper.ToErrorBody(nameErrors));
      }

      // Feature 114 (FR-010): a known value that goes to an image field must name an image.
      var imageErrors = await FindUnknownImageReferencesAsync(target, sequences, commands, images, ct).ConfigureAwait(false);
      if (imageErrors.Count > 0) {
        return Results.BadRequest(ParameterDtoMapper.ToErrorBody(imageErrors));
      }

      if (existing is null) {
        var created = await repo.CreateAsync(target).ConfigureAwait(false);
        var detail = await BuildDetailAsync(created, sequences).ConfigureAwait(false);
        return Results.Created($"{ApiRoutes.QueueTemplates}/{created.Id}", detail);
      }

      var saved = await repo.UpdateAsync(target).ConfigureAwait(false);
      return Results.Ok(await BuildDetailAsync(saved, sequences).ConfigureAwait(false));
    }).WithName("SaveQueueTemplate");

    group.MapDelete("{id}", async (string id, IQueueTemplateRepository repo) => {
      return await repo.DeleteAsync(id).ConfigureAwait(false)
        ? Results.NoContent()
        : NotFound();
    }).WithName("DeleteQueueTemplate");

    return app;
  }

  /// <summary>
  /// Finds each known value of the template that goes to an image field and names no image
  /// (feature 114, FR-010). The check looks at each entry, also a disabled entry, because an operator
  /// can enable it later. An entry whose sequence does not exist is not checked. Each distinct image
  /// id is checked one time.
  /// </summary>
  private static async Task<List<ParameterValidationIssue>> FindUnknownImageReferencesAsync(
      QueueTemplate template,
      ISequenceRepository sequences,
      ICommandRepository commands,
      IImageRepository images,
      CancellationToken ct) {
    var issues = new List<ParameterValidationIssue>();
    var exists = new Dictionary<string, bool>(StringComparer.Ordinal);

    for (var index = 0; index < template.Entries.Count; index++) {
      var entry = template.Entries[index];
      var sequence = await sequences.GetAsync(entry.SequenceId).ConfigureAwait(false);
      if (sequence is null) continue;

      var reachable = await QueuesEndpoints.CollectReachableCommandsAsync(sequence, commands).ConfigureAwait(false);
      foreach (var candidate in ParameterValidationService.FindImageValueCandidates(entry, index, sequence, reachable)) {
        if (!exists.TryGetValue(candidate.ImageId, out var found)) {
          found = await images.ExistsAsync(candidate.ImageId, ct).ConfigureAwait(false);
          exists[candidate.ImageId] = found;
        }

        if (found) continue;
        issues.Add(new ParameterValidationIssue(
            ParameterValidationCodes.UnknownImageReference,
            $"Entry {candidate.EntryIndex}: parameter '{candidate.ParameterName}' gives the image id '{candidate.ImageId}' to field '{candidate.FieldPath}', but no image has that id.",
            candidate.FieldPath,
            candidate.ParameterName,
            candidate.EntryIndex));
      }
    }

    return issues;
  }

  private static readonly System.Text.RegularExpressions.Regex NamePattern =
    new("^[A-Za-z0-9 _-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

  private static string? ValidateName(string? trimmedName) {
    if (string.IsNullOrWhiteSpace(trimmedName)) return "name is required";
    if (trimmedName.Length > 100) return "name must be 100 characters or fewer";
    if (!NamePattern.IsMatch(trimmedName)) {
      return "name may contain only letters, digits, spaces, hyphens, and underscores";
    }
    return null;
  }

  private static bool TryParseScheduleType(string? raw, out ScheduleType result) {
    if (string.IsNullOrWhiteSpace(raw)) {
      result = ScheduleType.OncePerRun;
      return true;
    }
    return Enum.TryParse(raw, ignoreCase: true, out result)
           && Enum.IsDefined(result);
  }

  private static QueueTemplateSummaryResponse BuildSummary(QueueTemplate template) => new() {
    Id = template.Id,
    Name = template.Name,
    EntryCount = template.Entries.Count,
    CreatedAt = template.CreatedAt,
    UpdatedAt = template.UpdatedAt
  };

  private static async Task<QueueTemplateDetailResponse> BuildDetailAsync(QueueTemplate template, ISequenceRepository sequences) {
    var allSequences = await sequences.ListAsync().ConfigureAwait(false);
    var namesById = allSequences.ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
    var detail = new QueueTemplateDetailResponse {
      Id = template.Id,
      Name = template.Name,
      EntryCount = template.Entries.Count,
      CreatedAt = template.CreatedAt,
      UpdatedAt = template.UpdatedAt
    };
    var sequencesById = allSequences.ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);
    foreach (var entry in template.Entries) {
      var found = namesById.TryGetValue(entry.SequenceId, out var name);
      detail.Entries.Add(new QueueTemplateEntryResponse {
        SequenceId = entry.SequenceId,
        SequenceName = found ? name : null,
        Stale = !found,
        ScheduleType = entry.ScheduleType.ToString(),
        TimerTimeOfDay = entry.TimerTimeOfDay is { } timeOfDay ? TimerTimeOfDayFormat.Format(timeOfDay) : null,
        TimerRelativeOffset = entry.TimerRelativeOffset is { } offset ? RelativeOffsetParser.Format(offset) : null,
        Enabled = entry.Enabled,
        // Feature 078
        ParameterValues = ParameterDtoMapper.ToResponseBindings(entry.ParameterValues),
        HasParameterOverrides = entry.ParameterValues.Count > 0,
        EffectiveParameters = BuildEffectiveParameters(entry, sequencesById.GetValueOrDefault(entry.SequenceId))
      });
    }
    return detail;
  }

  /// <summary>
  /// Per-parameter effective value and originating scope for one entry (feature 078, FR-028), so the
  /// operator can see what a parameter will resolve to — and which scope wins — without running
  /// anything. Queue built-ins appear with a null value because no run is in progress.
  /// </summary>
  /// <param name="entry">The template entry.</param>
  /// <param name="sequence">The referenced sequence, or null when the reference is stale.</param>
  private static Collection<GameBot.Service.Models.ParameterScopeEntryDto>? BuildEffectiveParameters(
      QueueTemplateEntry entry,
      GameBot.Domain.Commands.CommandSequence? sequence) {
    var declarations = sequence?.Parameters
        ?? (IReadOnlyCollection<ParameterDeclaration>)Array.Empty<ParameterDeclaration>();
    if (declarations.Count == 0 && entry.ParameterValues.Count == 0) return null;

    var scope = ParameterScope.Empty
        .Child(ParameterScopeLayers.Sequence, null, declarations)
        .Child(ParameterScopeLayers.Entry, entry.ParameterValues, null);
    return ParameterDtoMapper.ToResponseScopeEntries(scope.Describe());
  }

  private static IResult NotFound() => Error(404, "not_found", "Queue template not found");

  private static IResult Error(int status, string code, string message, string? hint = null) =>
    Results.Json(new { error = new { code, message, hint } }, statusCode: status);
}
