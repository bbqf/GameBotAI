using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.SequenceExecution;
using GameBot.Service.Services.StepThrough;
using GameBot.Service.Swagger;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 142 (FR-022): every type in <see cref="SequenceActionTypes.All"/> is wired into every place that must know
/// it. A new action type that misses a place fails here and the message names the type and the place. Before this
/// test the six places were found by search, and a miss showed up as a 500 at save time.
/// </summary>
public sealed class SequenceActionTypeSyncTests {
  // The types that are not service-level actions: they use the device or a command. A new type is added here
  // only when it is not service-level (SequenceRunner.IsServiceLevelAction is false for it on purpose).
  private static readonly HashSet<string> NonServiceTypes = new(StringComparer.OrdinalIgnoreCase) {
    ActionTypes.Command,
    ActionTypes.Tap,
    ActionTypes.Swipe,
    ActionTypes.Key,
    ActionTypes.ConnectToGame,
    ActionTypes.EnsureGameRunning,
    ActionTypes.GoToHomeScreen,
    ActionTypes.EnsureEmulatorRunning,
    ActionTypes.WaitForImage
  };

  private sealed class StubCoordinator : ISelfRescheduleCoordinator {
    public SelfRescheduleResult ScheduleSelf(string queueId, string sequenceId, SelfRescheduleOption option, TimeOnly? timerTimeOfDay, TimeSpan? timerRelativeOffset, ParameterScope? scope = null, SelfRescheduleKeep keep = SelfRescheduleKeep.None, string? runId = null) =>
      throw new NotSupportedException();

    public SelfRescheduleCancelResult CancelSelf(string queueId, string sequenceId) => throw new NotSupportedException();

    public DateTimeOffset ResolveTimerFireAt(TimeOnly? timerTimeOfDay, TimeSpan? timerRelativeOffset) => DateTimeOffset.UtcNow;
  }

  public static IEnumerable<object[]> AllTypes() => SequenceActionTypes.All.Select(t => new object[] { t });

  private static SequenceActionPayload Empty(string type) => new() { Type = type };

  [Theory]
  [MemberData(nameof(AllTypes))]
  public void TypeHasAnOpenApiPayloadDescription(string type) {
    PrimitiveActionSchemaFilter.PayloadDescriptions.Should().ContainKey(type,
      $"action type '{type}' needs an entry in PrimitiveActionSchemaFilter.PayloadDescriptions (OpenAPI)");
    PrimitiveActionSchemaFilter.PayloadDescription.Should().Contain($"- {type}:");
  }

  [Theory]
  [MemberData(nameof(AllTypes))]
  public async Task TypeIsInTheFileSequenceRepositoryAllowList(string type) {
    var root = Path.Combine(Path.GetTempPath(), "gamebot-sync-" + Guid.NewGuid().ToString("N"));
    try {
      var repo = new FileSequenceRepository(root);
      var sequence = new CommandSequence { Id = "sync", Name = "sync" };
      sequence.SetSteps(new[] { new SequenceStep { StepId = "s1", StepType = SequenceStepType.Action, Action = Empty(type) } });

      Exception? thrown = null;
      try {
        await repo.CreateAsync(sequence);
      }
      catch (Exception ex) {
        thrown = ex;
      }

      (thrown?.Message ?? string.Empty).Should().NotContain("unsupported action type",
        $"action type '{type}' needs an entry in the FileSequenceRepository allow-list (supportedActionTypes)");
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  [Theory]
  [MemberData(nameof(AllTypes))]
  public void TypeIsKnownToTheStepValidationService(string type) {
    var step = new SequenceStep { StepId = "s1", StepType = SequenceStepType.Action, Action = Empty(type) };

    var errors = new SequenceStepValidationService().Validate(new[] { step });

    errors.Should().NotContain(e => e.Contains("is not a supported primitive action type"),
      $"action type '{type}' is not known to SequenceStepValidationService");
    new ActionPayloadValidationService().SupportedActionTypes.Should().Contain(type,
      $"action type '{type}' is not in ActionPayloadValidationService.SupportedActionTypes");
  }

  [Theory]
  [MemberData(nameof(AllTypes))]
  public void TypeIsServiceLevelOrInTheDocumentedNonServiceSet(string type) {
    var known = SequenceRunner.IsServiceLevelAction(Empty(type)) || NonServiceTypes.Contains(type);

    known.Should().BeTrue(
      $"action type '{type}' must be in SequenceRunner.IsServiceLevelAction, or in the non-service set of this test");
    if (NonServiceTypes.Contains(type)) {
      SequenceRunner.IsServiceLevelAction(Empty(type)).Should().BeFalse(
        $"action type '{type}' is in the non-service set but IsServiceLevelAction accepts it");
    }
  }

  [Theory]
  [MemberData(nameof(AllTypes))]
  public async Task ServiceLevelTypeIsRoutedByTheDispatcherAndPreviewedByStepThrough(string type) {
    if (!SequenceRunner.IsServiceLevelAction(Empty(type))) {
      return;
    }

    var service = new SequenceExecutionService(
      runner: null!, evalSvc: null!, imageVisibleConditionAdapter: null!, imageRepository: null!,
      executionLogService: null!, commandRepository: null!, sequenceRepository: null!, commandExecutor: null!,
      selfRescheduleCoordinator: null!, sessionManager: null!, ensureGameRunning: null!,
      ensureEmulatorRunning: null!, sessionService: null!, ocrOffsetResolver: null!);

    ActionDispatchResult? dispatched = null;
    Exception? dispatchError = null;
    try {
      dispatched = await service.DispatchActionAsync(Empty(type), "seq", null, null, ParameterScope.Empty, null, CancellationToken.None);
    }
    catch (Exception ex) {
      dispatchError = ex;
    }

    dispatchError.Should().BeNull($"the dispatcher must route service-level action type '{type}' to its own handler");
    dispatched!.Message.Should().NotContain("unsupported", $"the dispatcher has no route for action type '{type}'");

    var preview = StepThroughActionPreview.Describe(Empty(type), new StubCoordinator(), DateTimeOffset.UtcNow);
    preview.Message.Should().NotBe($"would run '{type}'",
      $"StepThroughActionPreview needs a description for service-level action type '{type}'");
  }
}
