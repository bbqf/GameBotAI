using System.Text.Json;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using GameBot.Domain.Services;
using GameBot.Domain.Vision;
using GameBot.Service.Contracts.Sequences;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

internal static class SequencesEndpoints {
  private static readonly JsonSerializerOptions PerStepRequestJsonOptions = new() { PropertyNameCaseInsensitive = true };
  private static readonly string[] LegacyBranchingErrors = { "entryStepId and links are no longer supported. Use per-step conditions on steps[].condition." };

  public static IEndpointRouteBuilder MapSequenceEndpoints(this IEndpointRouteBuilder app) {
    var sequences = app.MapGroup(ApiRoutes.Sequences).WithTags("Sequences");

    sequences.MapPost("", async (HttpRequest http, ISequenceRepository repo, ICommandRepository commandRepository, SequenceStepValidationService stepValidationService, IImageRepository imageRepository, CancellationToken ct) => {
      using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      if (HasLegacyBranchingFields(root)) {
        return Results.BadRequest(new {
          message = "Invalid sequence payload",
          errors = LegacyBranchingErrors
        });
      }

      var isPerStepCandidate = IsPerStepRequestCandidate(root);
      if (TryReadPerStepRequest(root, out var perStepRequest, out var perStepRequestError) && perStepRequest is not null) {
        var perStepSequence = new GameBot.Domain.Commands.CommandSequence {
          Id = string.Empty,
          Name = perStepRequest.Name.Trim(),
          Version = perStepRequest.Version > 0 ? perStepRequest.Version : 1,
          CreatedAt = DateTimeOffset.UtcNow,
          UpdatedAt = DateTimeOffset.UtcNow
        };

        var linearSteps = MapToLinearSteps(perStepRequest);
        await EnrichCommandReferencesAsync(linearSteps, commandRepository, existingSteps: null, ct).ConfigureAwait(false);
        var perStepValidationErrors = await ValidatePerStepForPersistenceAsync(linearSteps, stepValidationService, imageRepository, ct).ConfigureAwait(false);
        if (perStepValidationErrors.Count > 0) {
          return Results.BadRequest(new { message = "Invalid sequence payload", errors = perStepValidationErrors });
        }

        var existingSequences = await repo.ListAsync().ConfigureAwait(false);
        if (existingSequences.Count == 0) {
          perStepSequence.Version = 1;
        }

        perStepSequence.SetFlowGraph(null);
        perStepSequence.SetSteps(linearSteps);
        perStepSequence.InterStepDelayRangeMs = MapDelayRangeMs(perStepRequest.InterStepDelayRangeMs);
        var createdPerStep = await repo.CreateAsync(perStepSequence).ConfigureAwait(false);
        return Results.Created(new Uri($"{ApiRoutes.Sequences}/{createdPerStep.Id}", UriKind.Relative), await ToSequenceResponseAsync(createdPerStep, commandRepository, ct).ConfigureAwait(false));
      }

      if (isPerStepCandidate && !string.IsNullOrWhiteSpace(perStepRequestError)) {
        return Results.BadRequest(new { message = "Invalid sequence payload", errors = new[] { perStepRequestError } });
      }

      // Authoring shape: { name: string, steps?: string[] }
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String && !root.TryGetProperty("blocks", out _)) {
        var name = nameProp.GetString()!.Trim();
        var seq = new GameBot.Domain.Commands.CommandSequence { Id = string.Empty, Name = name, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        if (root.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array) {
          var order = 0;
          var steps = new List<GameBot.Domain.Commands.SequenceStep>();
          foreach (var el in stepsProp.EnumerateArray()) {
            if (el.ValueKind == System.Text.Json.JsonValueKind.String) {
              steps.Add(new GameBot.Domain.Commands.SequenceStep { Order = order++, CommandId = el.GetString()! });
            }
          }
          seq.SetSteps(steps);
        }
        var created = await repo.CreateAsync(seq).ConfigureAwait(false);
        return Results.Created(new Uri($"{ApiRoutes.Sequences}/{created.Id}", UriKind.Relative), await ToSequenceResponseAsync(created, commandRepository, ct).ConfigureAwait(false));
      }
      // Fallback to domain shape
      var seqDomain = JsonSerializer.Deserialize<GameBot.Domain.Commands.CommandSequence>(root);
      if (seqDomain is null) return Results.BadRequest(new { message = "Invalid sequence payload" });
      var errors = ValidateSequence(seqDomain);
      if (errors.Count > 0) return Results.BadRequest(new { message = "Invalid sequence", errors });
      seqDomain.CreatedAt = DateTimeOffset.UtcNow;
      seqDomain.UpdatedAt = seqDomain.CreatedAt;
      var createdDomain = await repo.CreateAsync(seqDomain).ConfigureAwait(false);
      return Results.Created($"{ApiRoutes.Sequences}/{createdDomain.Id}", createdDomain);
    }).Accepts<System.Text.Json.JsonElement>("application/json").WithName("CreateSequence");

    sequences.MapGet("{sequenceId}", async (ISequenceRepository repo, ICommandRepository commandRepository, string sequenceId, CancellationToken ct) => {
      var found = await repo.GetAsync(sequenceId).ConfigureAwait(false);
      if (found is null) return Results.NotFound();
      return Results.Ok(await ToSequenceResponseAsync(found, commandRepository, ct).ConfigureAwait(false));
    }).WithName("GetSequence");

    sequences.MapPut("{sequenceId}", async (HttpRequest http, ISequenceRepository repo, ICommandRepository commandRepository, SequenceStepValidationService stepValidationService, IImageRepository imageRepository, string sequenceId, CancellationToken ct) => {
      var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
      if (existing is null) return Results.NotFound();
      using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      if (HasLegacyBranchingFields(root)) {
        return Results.BadRequest(new {
          message = "Invalid sequence payload",
          errors = LegacyBranchingErrors
        });
      }

      if (root.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == System.Text.Json.JsonValueKind.Number) {
        var requestedVersion = versionProp.GetInt32();
        if (requestedVersion != existing.Version) {
          return Results.Conflict(new SequenceSaveConflictDto {
            SequenceId = existing.Id,
            CurrentVersion = existing.Version,
            Message = "Sequence has changed. Reload and retry your save."
          });
        }
      }
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String) {
        var name = nameProp.GetString()!.Trim();
        if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
      }
      var isPerStepCandidate = IsPerStepRequestCandidate(root);
      if (TryReadPerStepRequest(root, out var perStepRequest, out var perStepRequestError) && perStepRequest is not null) {
        existing.Name = string.IsNullOrWhiteSpace(perStepRequest.Name) ? existing.Name : perStepRequest.Name.Trim();
        var linearSteps = MapToLinearSteps(perStepRequest);
        await EnrichCommandReferencesAsync(linearSteps, commandRepository, existing.Steps, ct).ConfigureAwait(false);
        var perStepValidationErrors = await ValidatePerStepForPersistenceAsync(linearSteps, stepValidationService, imageRepository, ct).ConfigureAwait(false);
        if (perStepValidationErrors.Count > 0) {
          return Results.BadRequest(new { message = "Invalid sequence payload", errors = perStepValidationErrors });
        }

        existing.SetFlowGraph(null);
        existing.SetSteps(linearSteps);
        existing.InterStepDelayRangeMs = MapDelayRangeMs(perStepRequest.InterStepDelayRangeMs);
      }
      else if (isPerStepCandidate && !string.IsNullOrWhiteSpace(perStepRequestError)) {
        return Results.BadRequest(new { message = "Invalid sequence payload", errors = new[] { perStepRequestError } });
      }
      if (root.TryGetProperty("steps", out var stepsProp)
          && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array
          && stepsProp.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String)) {
        var order = 0;
        var steps = new List<GameBot.Domain.Commands.SequenceStep>();
        foreach (var el in stepsProp.EnumerateArray()) {
          if (el.ValueKind == System.Text.Json.JsonValueKind.String) {
            steps.Add(new GameBot.Domain.Commands.SequenceStep { Order = order++, CommandId = el.GetString()! });
          }
        }
        existing.SetSteps(steps);
      }
      if (root.TryGetProperty("interStepDelayRangeMs", out var delayrProp) && delayrProp.ValueKind == System.Text.Json.JsonValueKind.Object) {
        var delayContract = JsonSerializer.Deserialize<DelayRangeMsContract>(delayrProp.GetRawText(), PerStepRequestJsonOptions);
        existing.InterStepDelayRangeMs = MapDelayRangeMs(delayContract);
      }
      existing.Version += 1;
      existing.UpdatedAt = DateTimeOffset.UtcNow;
      var saved = await repo.UpdateAsync(existing).ConfigureAwait(false);
      return Results.Ok(await ToSequenceResponseAsync(saved, commandRepository, ct).ConfigureAwait(false));
    }).WithName("UpdateSequence");

    sequences.MapPatch("{sequenceId}", async (HttpRequest http, ISequenceRepository repo, ICommandRepository commandRepository, SequenceStepValidationService stepValidationService, IImageRepository imageRepository, string sequenceId, CancellationToken ct) => {
      var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
      if (existing is null) return Results.NotFound();
      using var doc = await System.Text.Json.JsonDocument.ParseAsync(http.Body, cancellationToken: ct).ConfigureAwait(false);
      var root = doc.RootElement;
      if (HasLegacyBranchingFields(root)) {
        return Results.BadRequest(new {
          message = "Invalid sequence payload",
          errors = LegacyBranchingErrors
        });
      }

      if (root.TryGetProperty("version", out var versionProp) && versionProp.ValueKind == System.Text.Json.JsonValueKind.Number) {
        var requestedVersion = versionProp.GetInt32();
        if (requestedVersion != existing.Version) {
          return Results.Conflict(new SequenceSaveConflictDto {
            SequenceId = existing.Id,
            CurrentVersion = existing.Version,
            Message = "Sequence has changed. Reload and retry your save."
          });
        }
      }
      if (root.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == System.Text.Json.JsonValueKind.String) {
        var name = nameProp.GetString()!.Trim();
        if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
      }
      var isPerStepCandidate = IsPerStepRequestCandidate(root);
      if (TryReadPerStepRequest(root, out var perStepRequest, out var perStepRequestError) && perStepRequest is not null) {
        existing.Name = string.IsNullOrWhiteSpace(perStepRequest.Name) ? existing.Name : perStepRequest.Name.Trim();
        var linearSteps = MapToLinearSteps(perStepRequest);
        await EnrichCommandReferencesAsync(linearSteps, commandRepository, existing.Steps, ct).ConfigureAwait(false);
        var perStepValidationErrors = await ValidatePerStepForPersistenceAsync(linearSteps, stepValidationService, imageRepository, ct).ConfigureAwait(false);
        if (perStepValidationErrors.Count > 0) {
          return Results.BadRequest(new { message = "Invalid sequence payload", errors = perStepValidationErrors });
        }

        existing.SetFlowGraph(null);
        existing.SetSteps(linearSteps);
        existing.InterStepDelayRangeMs = MapDelayRangeMs(perStepRequest.InterStepDelayRangeMs);
      }
      else if (isPerStepCandidate && !string.IsNullOrWhiteSpace(perStepRequestError)) {
        return Results.BadRequest(new { message = "Invalid sequence payload", errors = new[] { perStepRequestError } });
      }
      if (root.TryGetProperty("steps", out var stepsProp)
          && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array
          && stepsProp.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String)) {
        var order = 0;
        var steps = new List<GameBot.Domain.Commands.SequenceStep>();
        foreach (var el in stepsProp.EnumerateArray()) {
          if (el.ValueKind == System.Text.Json.JsonValueKind.String) {
            steps.Add(new GameBot.Domain.Commands.SequenceStep { Order = order++, CommandId = el.GetString()! });
          }
        }
        existing.SetSteps(steps);
      }
      if (root.TryGetProperty("interStepDelayRangeMs", out var delayProp) && delayProp.ValueKind == System.Text.Json.JsonValueKind.Object) {
        var delayContract = JsonSerializer.Deserialize<DelayRangeMsContract>(delayProp.GetRawText(), PerStepRequestJsonOptions);
        existing.InterStepDelayRangeMs = MapDelayRangeMs(delayContract);
      }
      existing.Version += 1;
      existing.UpdatedAt = DateTimeOffset.UtcNow;
      var saved = await repo.UpdateAsync(existing).ConfigureAwait(false);
      return Results.Ok(await ToSequenceResponseAsync(saved, commandRepository, ct).ConfigureAwait(false));
    }).WithName("PatchSequence");

    sequences.MapPost("{sequenceId}/validate", async (string sequenceId, SequenceFlowUpsertRequestDto request, ISequenceFlowValidator validator, ISequenceRepository repo, SequenceStepValidationService stepValidationService) => {
      var graph = MapToFlowGraph(sequenceId, request);
      var flowResult = validator.Validate(graph);

      // Also run step-level validation (includes loop rules) on persisted steps
      var stepErrors = new List<string>();
      var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
      if (existing is not null) {
        stepErrors.AddRange(stepValidationService.Validate(existing.Steps));
      }

      var allErrors = flowResult.Errors.Concat(stepErrors).ToArray();
      if (allErrors.Length > 0) {
        return Results.BadRequest(new { valid = false, errors = allErrors });
      }

      return Results.Ok(new { valid = true, errors = Array.Empty<string>() });
    }).WithName("ValidateSequence");

    sequences.MapGet("", async (ISequenceRepository repo) => {
      var list = await repo.ListAsync().ConfigureAwait(false);
      var resp = list.Select(s => new { id = s.Id, name = s.Name, steps = s.Steps.Select(x => x.CommandId).ToArray() });
      return Results.Ok(resp);
    }).WithName("ListSequences");

    sequences.MapDelete("{sequenceId}", async (ISequenceRepository repo, string sequenceId) => {
      var existing = await repo.GetAsync(sequenceId).ConfigureAwait(false);
      if (existing is null) return Results.NotFound(new { error = new { code = "not_found", message = "Sequence not found", hint = (string?)null } });
      var ok = await repo.DeleteAsync(sequenceId).ConfigureAwait(false);
      return ok ? Results.NoContent() : Results.NotFound(new { error = new { code = "not_found", message = "Sequence not found", hint = (string?)null } });
    }).WithName("DeleteSequence");

    sequences.MapPost("{sequenceId}/execute", async (
      GameBot.Service.Services.SequenceExecution.ISequenceExecutionService sequenceExecution,
      string sequenceId,
      HttpContext httpContext,
      CancellationToken ct) => {
        // Read optional sessionId from request body
        string? sessionId = null;
        try {
          var body = await httpContext.Request.ReadFromJsonAsync<SequenceExecuteRequest>(ct).ConfigureAwait(false);
          sessionId = body?.SessionId;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) {
          // empty body, malformed JSON, or missing content-type — no sessionId override
        }

        var res = await sequenceExecution.ExecuteAsync(sequenceId, sessionId, parentContext: null, ct).ConfigureAwait(false);
        return Results.Ok(res);
      }).WithName("ExecuteSequence").WithTags("Sequences");

    return app;
  }

  private static async Task<object> ToSequenceResponseAsync(GameBot.Domain.Commands.CommandSequence sequence, ICommandRepository commandRepository, CancellationToken ct) {
    var commandLookup = await BuildCommandLookupAsync(commandRepository, ct).ConfigureAwait(false);
    return ToSequenceResponse(sequence, commandLookup);
  }

  private static object ToSequenceResponse(GameBot.Domain.Commands.CommandSequence sequence, IReadOnlyDictionary<string, string> commandLookup) {
    if (sequence.FlowSteps.Count > 0 || sequence.FlowLinks.Count > 0) {
      var flowSteps = sequence.FlowSteps.Select(step => new {
        stepId = step.StepId,
        label = step.Label,
        stepType = step.StepType.ToString().ToLowerInvariant(),
        payloadRef = step.PayloadRef,
        iterationLimit = step.IterationLimit,
        condition = MapConditionToDto(step.Condition)
      }).ToArray();

      var flowLinks = sequence.FlowLinks.Select(link => new {
        linkId = link.LinkId,
        sourceStepId = link.SourceStepId,
        targetStepId = link.TargetStepId,
        branchType = link.BranchType.ToString().ToLowerInvariant()
      }).ToArray();

      return new {
        id = sequence.Id,
        name = sequence.Name,
        version = sequence.Version,
        entryStepId = sequence.EntryStepId,
        steps = flowSteps,
        links = flowLinks,
        interStepDelayRangeMs = sequence.InterStepDelayRangeMs is not null
          ? new { min = sequence.InterStepDelayRangeMs.Min, max = sequence.InterStepDelayRangeMs.Max }
          : null
      };
    }

    if (sequence.Steps.Any(step => !string.IsNullOrWhiteSpace(step.StepId) || step.Action is not null || step.Condition is not null)) {
      return new {
        id = sequence.Id,
        name = sequence.Name,
        version = sequence.Version,
        steps = sequence.Steps.Select(step => MapStepToDto(step, commandLookup)).ToArray(),
        interStepDelayRangeMs = sequence.InterStepDelayRangeMs is not null
          ? new { min = sequence.InterStepDelayRangeMs.Min, max = sequence.InterStepDelayRangeMs.Max }
          : null
      };
    }

    return new {
      id = sequence.Id,
      name = sequence.Name,
      version = sequence.Version,
      steps = sequence.Steps.Select(step => step.CommandId).ToArray(),
      interStepDelayRangeMs = sequence.InterStepDelayRangeMs is not null
        ? new { min = sequence.InterStepDelayRangeMs.Min, max = sequence.InterStepDelayRangeMs.Max }
        : null
    };
  }

  private static object? MapConditionToDto(ConditionExpression? expression) {
    if (expression is null) {
      return null;
    }

    return new {
      nodeType = expression.NodeType.ToString().ToLowerInvariant(),
      children = expression.Children.Select(MapConditionToDto).Where(child => child is not null).ToArray(),
      operand = expression.Operand is null
        ? null
        : new {
          operandType = expression.Operand.OperandType.ToString()
            .ToLowerInvariant()
            .Replace("commandoutcome", "command-outcome", StringComparison.Ordinal)
            .Replace("imagedetection", "image-detection", StringComparison.Ordinal),
          targetRef = expression.Operand.TargetRef,
          expectedState = expression.Operand.ExpectedState,
          threshold = expression.Operand.Threshold
        }
    };
  }

  private static object? MapPerStepConditionToDto(SequenceStepCondition? condition) {
    return condition switch {
      ImageVisibleStepCondition imageVisible => new {
        type = "imageVisible",
        imageId = imageVisible.ImageId,
        minSimilarity = imageVisible.MinSimilarity,
        negate = imageVisible.Negate
      },
      CommandOutcomeStepCondition commandOutcome => new {
        type = "commandOutcome",
        stepRef = commandOutcome.StepRef,
        expectedState = commandOutcome.ExpectedState,
        negate = commandOutcome.Negate
      },
      _ => null
    };
  }

  private static object MapStepToDto(SequenceStep step, IReadOnlyDictionary<string, string> commandLookup) {
    var stepType = step.StepType switch {
      SequenceStepType.Loop => "Loop",
      SequenceStepType.Break => "Break",
      SequenceStepType.If => "If",
      _ => "Action"
    };

    return new {
      stepId = step.StepId,
      label = step.Label,
      stepType,
      commandReference = MapCommandReferenceToDto(step, commandLookup),
      primitiveAction = step.Action is null
        ? null
        : new {
          type = step.Action.Type,
          schemaVersion = step.Action.SchemaVersion,
          payload = step.Action.Parameters
        },
      condition = MapPerStepConditionToDto(step.Condition),
      loop = MapLoopConfigToDto(step.Loop),
      body = step.Body.Count > 0 ? step.Body.Select(child => MapStepToDto(child, commandLookup)).ToArray() : null,
      breakCondition = MapPerStepConditionToDto(step.BreakCondition),
      @if = step.If is null ? null : new { condition = MapPerStepConditionToDto(step.If.Condition) },
      elseBody = step.ElseBody?.Select(child => MapStepToDto(child, commandLookup)).ToArray()
    };
  }

  private static object? MapCommandReferenceToDto(SequenceStep step, IReadOnlyDictionary<string, string> commandLookup) {
    if (!IsCommandBackedStep(step)) {
      return null;
    }

    var commandId = step.CommandId?.Trim();
    if (string.IsNullOrWhiteSpace(commandId)) {
      return null;
    }

    commandLookup.TryGetValue(commandId, out var resolvedName);
    var snapshotName = !string.IsNullOrWhiteSpace(step.CommandReference?.CommandName)
      ? step.CommandReference!.CommandName!.Trim()
      : resolvedName;

    return new {
      commandId,
      commandName = snapshotName,
      isResolved = resolvedName is not null
    };
  }

  private static object? MapLoopConfigToDto(LoopConfig? loop) {
    return loop switch {
      CountLoopConfig count => new {
        loopType = "count",
        count = count.Count,
        maxIterations = count.MaxIterations
      },
      WhileLoopConfig while_ => new {
        loopType = "while",
        condition = MapPerStepConditionToDto(while_.Condition),
        maxIterations = while_.MaxIterations
      },
      RepeatUntilLoopConfig repeatUntil => new {
        loopType = "repeatUntil",
        condition = MapPerStepConditionToDto(repeatUntil.Condition),
        maxIterations = repeatUntil.MaxIterations
      },
      _ => null
    };
  }

  private static bool HasLegacyBranchingFields(System.Text.Json.JsonElement root) {
    return root.TryGetProperty("entryStepId", out _)
           || root.TryGetProperty("links", out _);
  }

  private static bool IsPerStepRequestCandidate(System.Text.Json.JsonElement root) {
    if (HasLegacyBranchingFields(root)) {
      return false;
    }

    if (!root.TryGetProperty("steps", out var stepsProp) || stepsProp.ValueKind != JsonValueKind.Array) {
      return false;
    }

    var firstStep = stepsProp.EnumerateArray().FirstOrDefault();
    return firstStep.ValueKind == JsonValueKind.Object
      && (firstStep.TryGetProperty("primitiveAction", out _) || firstStep.TryGetProperty("stepType", out _));
  }

  private static bool TryReadPerStepRequest(System.Text.Json.JsonElement root, out SequenceUpsertContract? request, out string? error) {
    request = null;
    error = null;

    var isPerStepCandidate = IsPerStepRequestCandidate(root);
    if (!isPerStepCandidate) {
      return false;
    }

    if (!root.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String) {
      error = "name is required for sequence payload.";
      return false;
    }

    if (!root.TryGetProperty("steps", out var stepsProp) || stepsProp.ValueKind != JsonValueKind.Array) {
      error = "steps array is required for sequence payload.";
      return false;
    }

    foreach (var stepElement in stepsProp.EnumerateArray()) {
      if (stepElement.ValueKind != JsonValueKind.Object) {
        error = "each step must be an object.";
        return false;
      }

      if (!stepElement.TryGetProperty("stepId", out var stepIdProp) || stepIdProp.ValueKind != JsonValueKind.String) {
        error = "each step must include string stepId.";
        return false;
      }

      var hasStepType = stepElement.TryGetProperty("stepType", out var stepTypeProp) && stepTypeProp.ValueKind == JsonValueKind.String;
      var stepTypeValue = hasStepType ? stepTypeProp.GetString()?.Trim().ToLowerInvariant() : null;
      var isLoopOrBreak = stepTypeValue is "loop" or "break" or "if";

      if (!isLoopOrBreak && (!stepElement.TryGetProperty("primitiveAction", out var primitiveActionProp) || primitiveActionProp.ValueKind != JsonValueKind.Object)) {
        error = "each action step must include primitiveAction object.";
        return false;
      }
    }

    try {
      request = JsonSerializer.Deserialize<SequenceUpsertContract>(
        root.GetRawText(),
        PerStepRequestJsonOptions);
    }
    catch (JsonException ex) {
      error = string.IsNullOrWhiteSpace(ex.Message) ? "Malformed sequence payload." : ex.Message;
      return false;
    }

    if (request is null) {
      error = "Malformed sequence payload.";
      return false;
    }

    if (request.Steps is null || request.Steps.Count == 0) {
      error = "steps must contain at least one step.";
      return false;
    }

    return true;
  }

  private static List<string> ValidateSequence(GameBot.Domain.Commands.CommandSequence seq) {
    var errs = new List<string>();
    // Validate blocks if present
    if (seq.Blocks is { Count: > 0 }) {
      foreach (var b in seq.Blocks) {
        ValidateBlock(b, errs, isTopLevel: true);
      }
    }
    // Validate inter-step delay range if present
    if (seq.InterStepDelayRangeMs is not null) {
      if (seq.InterStepDelayRangeMs.Min < 0) {
        errs.Add("InterStepDelayRangeMs.Min must be >= 0.");
      }
      if (seq.InterStepDelayRangeMs.Min > seq.InterStepDelayRangeMs.Max) {
        errs.Add("InterStepDelayRangeMs.Min must be <= Max.");
      }
    }
    return errs;
  }

  private static void ValidateBlock(object blockObj, List<string> errs, bool isTopLevel) {
    if (blockObj is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object) {
      string? type = null;
      if (je.TryGetProperty("type", out var tProp) && tProp.ValueKind == System.Text.Json.JsonValueKind.String) {
        type = tProp.GetString();
      }
      if (string.IsNullOrWhiteSpace(type)) {
        errs.Add("Block missing required 'type'.");
        return;
      }
      var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "repeatCount", "repeatUntil", "while", "ifElse" };
      if (!allowed.Contains(type)) {
        errs.Add($"Unsupported block type '{type}'.");
        return;
      }

      // Common: steps array for all but else-only
      if (je.TryGetProperty("steps", out var stepsProp) && stepsProp.ValueKind == System.Text.Json.JsonValueKind.Array) {
        foreach (var item in stepsProp.EnumerateArray()) {
          // item can be a Step (object without 'type') or a nested Block (object with 'type')
          if (item.ValueKind == System.Text.Json.JsonValueKind.Object && item.TryGetProperty("type", out var nestedType)) {
            ValidateBlock(item, errs, isTopLevel: false);
          }
        }
      }

      if (type.Equals("ifElse", StringComparison.OrdinalIgnoreCase)) {
        if (!je.TryGetProperty("condition", out var cond) || cond.ValueKind != System.Text.Json.JsonValueKind.Object) {
          errs.Add("ifElse block requires 'condition'.");
        }
        // Validate elseSteps only for ifElse
        if (je.TryGetProperty("elseSteps", out var elseProp) && elseProp.ValueKind == System.Text.Json.JsonValueKind.Array) {
          foreach (var item in elseProp.EnumerateArray()) {
            if (item.ValueKind == System.Text.Json.JsonValueKind.Object && item.TryGetProperty("type", out var nestedType)) {
              ValidateBlock(item, errs, isTopLevel: false);
            }
          }
        }
      }
      else if (type.Equals("repeatUntil", StringComparison.OrdinalIgnoreCase) || type.Equals("while", StringComparison.OrdinalIgnoreCase)) {
        if (!je.TryGetProperty("condition", out var cond) || cond.ValueKind != System.Text.Json.JsonValueKind.Object) {
          errs.Add($"{type} block requires 'condition'.");
        }
        var hasTimeout = je.TryGetProperty("timeoutMs", out var to) && to.ValueKind == System.Text.Json.JsonValueKind.Number && to.GetInt32() >= 0;
        var hasMaxIter = je.TryGetProperty("maxIterations", out var mi) && mi.ValueKind == System.Text.Json.JsonValueKind.Number && mi.GetInt32() >= 1;
        if (!hasTimeout && !hasMaxIter) {
          errs.Add($"{type} block must set 'timeoutMs' or 'maxIterations'.");
        }
        if (je.TryGetProperty("cadenceMs", out var cadence) && cadence.ValueKind == System.Text.Json.JsonValueKind.Number) {
          var c = cadence.GetInt32();
          if (c < 50 || c > 5000) {
            errs.Add($"{type} cadenceMs out of bounds (50-5000): {c}.");
          }
        }
      }
      else if (type.Equals("repeatCount", StringComparison.OrdinalIgnoreCase)) {
        if (!je.TryGetProperty("maxIterations", out var mi) || mi.ValueKind != System.Text.Json.JsonValueKind.Number || mi.GetInt32() < 0) {
          errs.Add("repeatCount requires non-negative 'maxIterations'.");
        }
        if (je.TryGetProperty("cadenceMs", out var cadence) && cadence.ValueKind == System.Text.Json.JsonValueKind.Number) {
          var c = cadence.GetInt32();
          if (c != 0 && (c < 50 || c > 5000)) {
            errs.Add($"repeatCount cadenceMs must be 0 or within 50-5000: {c}.");
          }
        }
      }

      // If a non-ifElse block provides elseSteps, flag as error (T015)
      if (!type.Equals("ifElse", StringComparison.OrdinalIgnoreCase) && je.TryGetProperty("elseSteps", out var elseAny) && elseAny.ValueKind == System.Text.Json.JsonValueKind.Array) {
        errs.Add($"'elseSteps' is only valid for ifElse blocks, not '{type}'.");
      }
    }
  }

  private static SequenceFlowGraph MapToFlowGraph(string sequenceId, SequenceFlowUpsertRequestDto request) {
    var graph = new SequenceFlowGraph {
      SequenceId = sequenceId,
      Name = request.Name,
      Version = request.Version,
      EntryStepId = request.EntryStepId
    };

    graph.SetSteps(request.Steps.Select(step => new FlowStep {
      StepId = step.StepId,
      Label = step.Label,
      StepType = step.StepType.Trim().ToLowerInvariant() switch {
        "action" => FlowStepType.Action,
        "command" => FlowStepType.Command,
        "condition" => FlowStepType.Condition,
        "terminal" => FlowStepType.Terminal,
        _ => throw new InvalidOperationException($"Unsupported flow step type '{step.StepType}'.")
      },
      PayloadRef = step.PayloadRef,
      IterationLimit = step.IterationLimit,
      Condition = MapCondition(step.Condition)
    }));

    graph.SetLinks(request.Links.Select(link => new BranchLink {
      LinkId = link.LinkId,
      SourceStepId = link.SourceStepId,
      TargetStepId = link.TargetStepId,
      BranchType = link.BranchType.Trim().ToLowerInvariant() switch {
        "next" => BranchType.Next,
        "true" => BranchType.True,
        "false" => BranchType.False,
        _ => BranchType.Next
      }
    }));

    return graph;
  }

  private static List<SequenceStep> MapToLinearSteps(SequenceUpsertContract request) {
    var result = new List<SequenceStep>(request.Steps.Count);
    for (var index = 0; index < request.Steps.Count; index++) {
      var step = request.Steps[index];
      var parsedStepType = ParseStepType(step.StepType);

      if (parsedStepType == SequenceStepType.Loop) {
        var mapped = new SequenceStep {
          Order = index,
          StepId = step.StepId,
          Label = step.Label,
          StepType = SequenceStepType.Loop,
          Loop = MapLoopConfig(step.Loop),
          Body = MapBodySteps(step.Body)
        };
        result.Add(mapped);
        continue;
      }

      if (parsedStepType == SequenceStepType.Break) {
        var mapped = new SequenceStep {
          Order = index,
          StepId = step.StepId,
          Label = step.Label,
          StepType = SequenceStepType.Break,
          BreakCondition = MapPerStepCondition(step.BreakCondition)
        };
        result.Add(mapped);
        continue;
      }

      if (parsedStepType == SequenceStepType.If) {
        var mapped = new SequenceStep {
          Order = index,
          StepId = step.StepId,
          Label = step.Label,
          StepType = SequenceStepType.If,
          If = MapIfConfig(step.If),
          Body = MapBodySteps(step.Body),
          ElseBody = step.ElseBody is null ? null : MapBodySteps(step.ElseBody)
        };
        result.Add(mapped);
        continue;
      }

      {
        var mapped = new SequenceStep {
          Order = index,
          StepId = step.StepId,
          Label = step.Label,
          CommandId = step.StepId,
          CommandReference = MapCommandReference(step.CommandReference, step.StepId),
          StepType = SequenceStepType.Action,
          Action = step.PrimitiveAction is not null ? new SequenceActionPayload { Type = step.PrimitiveAction.Type, SchemaVersion = step.PrimitiveAction.SchemaVersion } : null,
          WaitForImage = MapWaitForImageConfig(step.PrimitiveAction),
          Condition = MapPerStepCondition(step.Condition)
        };

        if (step.PrimitiveAction is not null) {
          foreach (var parameter in step.PrimitiveAction.Payload) {
            mapped.Action!.Parameters[parameter.Key] = parameter.Value;
          }

          if (string.Equals(step.PrimitiveAction.Type, ActionTypes.Command, StringComparison.OrdinalIgnoreCase)
              && step.PrimitiveAction.Payload.TryGetValue("commandId", out var commandId)
              && commandId is not null
              && !string.IsNullOrWhiteSpace(commandId.ToString())) {
            mapped.CommandId = commandId.ToString()!;
            mapped.CommandReference = MapCommandReference(step.CommandReference, mapped.CommandId);
          }
        }

        result.Add(mapped);
      }
    }

    return result;
  }

  private static SequenceStepType ParseStepType(string? stepType) {
    if (string.IsNullOrWhiteSpace(stepType)) return SequenceStepType.Action;
    return stepType.Trim().ToLowerInvariant() switch {
      "loop" => SequenceStepType.Loop,
      "break" => SequenceStepType.Break,
      "if" => SequenceStepType.If,
      "action" => SequenceStepType.Action,
      "command" => SequenceStepType.Command,
      _ => SequenceStepType.Action
    };
  }

  private static IfConfig? MapIfConfig(IfConfigContract? contract) {
    var condition = contract is null ? null : MapPerStepCondition(contract.Condition);
    return condition is null ? null : new IfConfig { Condition = condition };
  }

  private static LoopConfig? MapLoopConfig(LoopConfigContract? contract) {
    return contract switch {
      CountLoopConfigContract count => new CountLoopConfig { Count = count.Count, MaxIterations = count.MaxIterations },
      WhileLoopConfigContract while_ => new WhileLoopConfig { Condition = MapPerStepCondition(while_.Condition)!, MaxIterations = while_.MaxIterations },
      RepeatUntilLoopConfigContract repeatUntil => new RepeatUntilLoopConfig { Condition = MapPerStepCondition(repeatUntil.Condition)!, MaxIterations = repeatUntil.MaxIterations },
      _ => null
    };
  }

  private static IReadOnlyList<SequenceStep> MapBodySteps(IReadOnlyList<SequenceStepContract>? body) {
    if (body is null || body.Count == 0) return Array.Empty<SequenceStep>();
    var result = new List<SequenceStep>(body.Count);
    for (var i = 0; i < body.Count; i++) {
      var child = body[i];
      var childType = ParseStepType(child.StepType);

      if (childType == SequenceStepType.Break) {
        result.Add(new SequenceStep {
          Order = i,
          StepId = child.StepId,
          Label = child.Label,
          StepType = SequenceStepType.Break,
          BreakCondition = MapPerStepCondition(child.BreakCondition)
        });
      }
      else if (childType == SequenceStepType.If) {
        // Loop bodies may contain if steps; validation rejects ifs nested inside if branches.
        result.Add(new SequenceStep {
          Order = i,
          StepId = child.StepId,
          Label = child.Label,
          StepType = SequenceStepType.If,
          If = MapIfConfig(child.If),
          Body = MapBodySteps(child.Body),
          ElseBody = child.ElseBody is null ? null : MapBodySteps(child.ElseBody)
        });
      }
      else {
        var mapped = new SequenceStep {
          Order = i,
          StepId = child.StepId,
          Label = child.Label,
          CommandId = child.StepId,
          CommandReference = MapCommandReference(child.CommandReference, child.StepId),
          StepType = SequenceStepType.Action,
          Action = child.PrimitiveAction is not null ? new SequenceActionPayload { Type = child.PrimitiveAction.Type, SchemaVersion = child.PrimitiveAction.SchemaVersion } : null,
          WaitForImage = MapWaitForImageConfig(child.PrimitiveAction),
          Condition = MapPerStepCondition(child.Condition)
        };
        if (child.PrimitiveAction is not null) {
          foreach (var parameter in child.PrimitiveAction.Payload) {
            mapped.Action!.Parameters[parameter.Key] = parameter.Value;
          }
          if (string.Equals(child.PrimitiveAction.Type, ActionTypes.Command, StringComparison.OrdinalIgnoreCase)
              && child.PrimitiveAction.Payload.TryGetValue("commandId", out var cid)
              && cid is not null
              && !string.IsNullOrWhiteSpace(cid.ToString())) {
            mapped.CommandId = cid.ToString()!;
            mapped.CommandReference = MapCommandReference(child.CommandReference, mapped.CommandId);
          }
        }
        result.Add(mapped);
      }
    }
    return result;
  }

  private static IEnumerable<GameBot.Domain.Commands.SequenceStep> FlattenSequenceSteps(IEnumerable<GameBot.Domain.Commands.SequenceStep> steps) {
    foreach (var step in steps) {
      yield return step;

      if (step.Body.Count > 0) {
        foreach (var child in FlattenSequenceSteps(step.Body)) {
          yield return child;
        }
      }

      if (step.ElseBody is { Count: > 0 }) {
        foreach (var child in FlattenSequenceSteps(step.ElseBody)) {
          yield return child;
        }
      }
    }
  }

  private static SequenceCommandReference? MapCommandReference(SequenceCommandReferenceContract? contract, string commandId) {
    var normalizedId = string.IsNullOrWhiteSpace(commandId) ? contract?.CommandId?.Trim() : commandId.Trim();
    if (string.IsNullOrWhiteSpace(normalizedId)) {
      return null;
    }

    var normalizedName = string.IsNullOrWhiteSpace(contract?.CommandName) ? null : contract!.CommandName!.Trim();
    return new SequenceCommandReference {
      CommandId = normalizedId,
      CommandName = normalizedName
    };
  }

  private static bool IsCommandBackedStep(SequenceStep step) {
    return step.StepType != SequenceStepType.Loop
      && step.StepType != SequenceStepType.Break
      && string.Equals(step.Action?.Type, ActionTypes.Command, StringComparison.OrdinalIgnoreCase)
      && !string.IsNullOrWhiteSpace(step.CommandId);
  }

  private static async Task<IReadOnlyDictionary<string, string>> BuildCommandLookupAsync(ICommandRepository commandRepository, CancellationToken ct) {
    var commands = await commandRepository.ListAsync(ct).ConfigureAwait(false);
    return commands
      .Where(command => !string.IsNullOrWhiteSpace(command.Id))
      .GroupBy(command => command.Id, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(group => group.Key, group => group.First().Name, StringComparer.OrdinalIgnoreCase);
  }

  private static async Task EnrichCommandReferencesAsync(IReadOnlyList<SequenceStep> steps, ICommandRepository commandRepository, IReadOnlyList<SequenceStep>? existingSteps, CancellationToken ct) {
    var commandLookup = await BuildCommandLookupAsync(commandRepository, ct).ConfigureAwait(false);
    var existingLookup = FlattenSequenceSteps(existingSteps ?? Array.Empty<SequenceStep>())
      .Where(step => IsCommandBackedStep(step))
      .GroupBy(step => step.StepId, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

    EnrichCommandReferences(steps, commandLookup, existingLookup);
  }

  private static void EnrichCommandReferences(IReadOnlyList<SequenceStep> steps, IReadOnlyDictionary<string, string> commandLookup, IReadOnlyDictionary<string, SequenceStep> existingLookup) {
    foreach (var step in steps) {
      if (IsCommandBackedStep(step)) {
        if (commandLookup.TryGetValue(step.CommandId, out var liveName)) {
          step.CommandReference = new SequenceCommandReference {
            CommandId = step.CommandId,
            CommandName = liveName
          };
        }
        else if (!string.IsNullOrWhiteSpace(step.CommandReference?.CommandName)) {
          step.CommandReference = new SequenceCommandReference {
            CommandId = step.CommandId,
            CommandName = step.CommandReference.CommandName!.Trim()
          };
        }
        else if (existingLookup.TryGetValue(step.StepId, out var existingStep)
                 && string.Equals(existingStep.CommandId, step.CommandId, StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrWhiteSpace(existingStep.CommandReference?.CommandName)) {
          step.CommandReference = new SequenceCommandReference {
            CommandId = step.CommandId,
            CommandName = existingStep.CommandReference.CommandName!.Trim()
          };
        }
        else {
          step.CommandReference = new SequenceCommandReference {
            CommandId = step.CommandId,
            CommandName = null
          };
        }
      }

      if (step.Body.Count > 0) {
        EnrichCommandReferences(step.Body, commandLookup, existingLookup);
      }
    }
  }

  private static WaitForImageConfig? MapWaitForImageConfig(PrimitiveActionRequest? primitiveAction) {
    if (primitiveAction is null || !string.Equals(primitiveAction.Type, ActionTypes.WaitForImage, StringComparison.OrdinalIgnoreCase)) {
      return null;
    }

    var timeoutMs = 1000;
    if (primitiveAction.Payload.TryGetValue("timeoutMs", out var timeoutValue)
        && TryReadInt32(timeoutValue, out var parsedTimeout)) {
      timeoutMs = parsedTimeout;
    }

    return new WaitForImageConfig {
      TimeoutMs = timeoutMs,
      DetectionTarget = MapWaitForImageDetectionTarget(primitiveAction.Payload)
    };
  }

  private static DetectionTarget? MapWaitForImageDetectionTarget(Dictionary<string, object> payload) {
    if (!payload.TryGetValue("detectionTarget", out var detectionTargetValue)
        || detectionTargetValue is not JsonElement detectionTargetElement
        || detectionTargetElement.ValueKind != JsonValueKind.Object) {
      return null;
    }

    if (!detectionTargetElement.TryGetProperty("referenceImageId", out var referenceImageIdElement)
        || referenceImageIdElement.ValueKind != JsonValueKind.String) {
      return null;
    }

    var referenceImageId = referenceImageIdElement.GetString();
    if (string.IsNullOrWhiteSpace(referenceImageId)) {
      return null;
    }

    var confidence = detectionTargetElement.TryGetProperty("confidence", out var confidenceElement)
                     && confidenceElement.ValueKind == JsonValueKind.Number
                     && confidenceElement.TryGetDouble(out var parsedConfidence)
      ? parsedConfidence
      : 0.8;
    var offsetX = detectionTargetElement.TryGetProperty("offsetX", out var offsetXElement)
                  && offsetXElement.ValueKind == JsonValueKind.Number
                  && offsetXElement.TryGetInt32(out var parsedOffsetX)
      ? parsedOffsetX
      : 0;
    var offsetY = detectionTargetElement.TryGetProperty("offsetY", out var offsetYElement)
                  && offsetYElement.ValueKind == JsonValueKind.Number
                  && offsetYElement.TryGetInt32(out var parsedOffsetY)
      ? parsedOffsetY
      : 0;
    var selectionStrategy = detectionTargetElement.TryGetProperty("selectionStrategy", out var selectionStrategyElement)
                            && selectionStrategyElement.ValueKind == JsonValueKind.String
                            && string.Equals(selectionStrategyElement.GetString(), nameof(DetectionSelectionStrategy.FirstMatch), StringComparison.OrdinalIgnoreCase)
      ? DetectionSelectionStrategy.FirstMatch
      : DetectionSelectionStrategy.HighestConfidence;

    return new DetectionTarget(referenceImageId, confidence, offsetX, offsetY, selectionStrategy);
  }

  private static bool TryReadInt32(object? value, out int result) {
    switch (value) {
      case int intValue:
        result = intValue;
        return true;
      case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
        result = (int)longValue;
        return true;
      case JsonElement element when element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var parsed):
        result = parsed;
        return true;
      case string text when int.TryParse(text, out var parsedText):
        result = parsedText;
        return true;
      default:
        result = 0;
        return false;
    }
  }

  private static SequenceStepCondition? MapPerStepCondition(SequenceStepConditionContract? condition) {
    return condition switch {
      ImageVisibleConditionContract imageVisible => new ImageVisibleStepCondition {
        ImageId = imageVisible.ImageId,
        MinSimilarity = imageVisible.MinSimilarity,
        Negate = imageVisible.Negate
      },
      CommandOutcomeConditionContract commandOutcome => new CommandOutcomeStepCondition {
        StepRef = commandOutcome.StepRef,
        ExpectedState = commandOutcome.ExpectedState,
        Negate = commandOutcome.Negate
      },
      _ => null
    };
  }

  private static ConditionExpression? MapCondition(ConditionExpressionDto? dto) {
    if (dto is null) {
      return null;
    }

    var expression = new ConditionExpression {
      NodeType = dto.NodeType.Trim().ToLowerInvariant() switch {
        "and" => ConditionNodeType.And,
        "or" => ConditionNodeType.Or,
        "not" => ConditionNodeType.Not,
        "operand" => ConditionNodeType.Operand,
        _ => ConditionNodeType.Operand
      },
      Operand = dto.Operand is null
        ? null
        : new ConditionOperand {
          OperandType = dto.Operand.OperandType.Trim().ToLowerInvariant() switch {
            "command-outcome" => ConditionOperandType.CommandOutcome,
            "image-detection" => ConditionOperandType.ImageDetection,
            _ => ConditionOperandType.CommandOutcome
          },
          TargetRef = dto.Operand.TargetRef,
          ExpectedState = dto.Operand.ExpectedState,
          Threshold = dto.Operand.Threshold
        }
    };

    if (dto.Children is not null) {
      expression.SetChildren(dto.Children.Select(MapCondition).Where(c => c is not null).Select(c => c!));
    }

    return expression;
  }

  private static async Task<List<string>> ValidatePerStepForPersistenceAsync(
    List<SequenceStep> steps,
    SequenceStepValidationService stepValidationService,
    IImageRepository imageRepository,
    CancellationToken ct) {
    var errors = new List<string>();
    errors.AddRange(stepValidationService.Validate(steps));
    errors.AddRange(await ValidatePerStepImageReferencesAsync(steps, imageRepository, ct).ConfigureAwait(false));

    for (var index = 0; index < steps.Count; index++) {
      ct.ThrowIfCancellationRequested();
      var step = steps[index];
      var stepLabel = string.IsNullOrWhiteSpace(step.StepId) ? $"index:{index}" : step.StepId;
      if (step.Action is null) {
        continue;
      }

      if (string.IsNullOrWhiteSpace(step.Action.Type)) {
        errors.Add($"Step '{stepLabel}' action type is required.");
      }

      if (step.Condition is ImageVisibleStepCondition imageVisible
          && imageVisible.MinSimilarity is < 0 or > 1) {
        errors.Add($"Step '{stepLabel}' imageVisible minSimilarity must be within 0..1.");
      }
    }

    return errors;
  }

  private static async Task<IReadOnlyList<string>> ValidatePerStepImageReferencesAsync(
    IReadOnlyList<SequenceStep> steps,
    IImageRepository imageRepository,
    CancellationToken ct) {
    var errors = new List<string>();
    var missingByImageId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    var cache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    foreach (var step in steps.Where(step => step.Condition is ImageVisibleStepCondition)) {
      ct.ThrowIfCancellationRequested();
      var imageCondition = (ImageVisibleStepCondition)step.Condition!;
      var imageId = imageCondition.ImageId?.Trim();
      if (string.IsNullOrWhiteSpace(imageId)) {
        continue;
      }

      if (!cache.TryGetValue(imageId, out var exists)) {
        exists = await imageRepository.ExistsAsync(imageId, ct).ConfigureAwait(false);
        cache[imageId] = exists;
      }

      if (exists) {
        continue;
      }

      if (!missingByImageId.TryGetValue(imageId, out var stepsForImage)) {
        stepsForImage = new List<string>();
        missingByImageId[imageId] = stepsForImage;
      }

      stepsForImage.Add(string.IsNullOrWhiteSpace(step.StepId) ? $"index:{step.Order}" : step.StepId);
    }

    foreach (var missing in missingByImageId.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
      errors.Add($"Image reference '{missing.Key}' does not exist (used by: {string.Join(", ", missing.Value.Distinct(StringComparer.OrdinalIgnoreCase))}).");
    }

    return errors;
  }

  private static DelayRangeMs? MapDelayRangeMs(DelayRangeMsContract? contract) {
    if (contract is null) return null;
    return new DelayRangeMs { Min = contract.Min, Max = contract.Max };
  }
}
