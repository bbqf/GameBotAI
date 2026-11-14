using System;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.StepThrough;
using Xunit;

namespace GameBot.UnitTests.StepThrough;

/// <summary>Feature 175 (FR-009): the preview text of a WaitForImage step names the absent mode.</summary>
public sealed class WaitForImageAbsentPreviewTests {
  private sealed class StubCoordinator : ISelfRescheduleCoordinator {
    public SelfRescheduleResult ScheduleSelf(string queueId, string sequenceId, SelfRescheduleOption option, TimeOnly? timerTimeOfDay, TimeSpan? timerRelativeOffset, ParameterScope? scope = null, SelfRescheduleKeep keep = SelfRescheduleKeep.None, string? runId = null, SmartSchedulingRequest? smartScheduling = null) =>
      throw new NotSupportedException();

    public SelfRescheduleCancelResult CancelSelf(string queueId, string sequenceId) => throw new NotSupportedException();

    public DateTimeOffset ResolveTimerFireAt(TimeOnly? timerTimeOfDay, TimeSpan? timerRelativeOffset) => DateTimeOffset.UtcNow;
  }

  private static string Describe(object? until) {
    var action = new SequenceActionPayload { Type = ActionTypes.WaitForImage };
    if (until is not null) action.Parameters["until"] = until;
    return StepThroughActionPreview.Describe(action, new StubCoordinator(), DateTimeOffset.UtcNow).Message!;
  }

  [Fact]
  public void AbsentModeNamesTheAbsentWait() {
    Describe("absent").Should().Contain("until the image is gone");
  }

  [Fact]
  public void AbsentModeFromJsonElementNamesTheAbsentWait() {
    Describe(JsonSerializer.SerializeToElement("Absent")).Should().Contain("until the image is gone");
  }

  [Fact]
  public void PresentModeAndNoModeNameThePresentWait() {
    Describe("present").Should().Contain("until the image shows");
    Describe(null).Should().Contain("until the image shows");
  }
}
