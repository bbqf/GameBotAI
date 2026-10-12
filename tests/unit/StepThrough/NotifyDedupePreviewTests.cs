#pragma warning disable CA2007 // Test code: no synchronization context.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Notify;
using GameBot.Domain.Queues;
using GameBot.Service.Services.StepThrough;
using GameBot.UnitTests.Queues;
using Xunit;

namespace GameBot.UnitTests.StepThrough;

/// <summary>Feature 137 (FR-014): the preview of a notify step reads the store and writes nothing.</summary>
public sealed class NotifyDedupePreviewTests : IDisposable {
  private static readonly DateTimeOffset T0 = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
  private readonly string _root = Path.Combine(Path.GetTempPath(), "notify-preview-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _clock = new(T0);

  public void Dispose() {
    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
  }

  private static SequenceActionPayload Notify(bool dedupe = true) {
    var action = new SequenceActionPayload { Type = ActionTypes.Notify };
    action.Parameters["message"] = "Daily task timed out";
    if (dedupe) {
      action.Parameters["dedupeKey"] = "k";
      action.Parameters["within"] = "01:00:00";
    }

    return action;
  }

  private async Task<string?> DescribeAsync(SequenceActionPayload action, ISentAlertStore? store) =>
    (await StepThroughActionPreview.DescribeAsync(action, null!, T0, store, _clock, CancellationToken.None)).Message;

  [Fact]
  public async Task NoRecord_WouldSend_AndWritesNothing() {
    using var store = new FileSentAlertStore(_root);

    (await DescribeAsync(Notify(), store)).Should().StartWith("would send a notification");

    File.Exists(Path.Combine(store.FolderPath, FileSentAlertStore.FileName)).Should().BeFalse("a preview writes no record");
    (await store.HasLiveReservationAsync(NotifyPayload.AdhocScope, "k", T0)).Should().BeFalse("a preview makes no reservation");
  }

  [Fact]
  public async Task RecordInWindow_WouldSuppress() {
    using var store = new FileSentAlertStore(_root);
    var token = await store.TryReserveAsync(NotifyPayload.AdhocScope, "k", T0.AddHours(-1), T0.AddMinutes(-10));
    await store.CommitAsync(token!, T0.AddMinutes(-10));

    (await DescribeAsync(Notify(), store)).Should().StartWith("would suppress the notification: sent at");
  }

  [Fact]
  public async Task LiveReservation_WouldSuppress() {
    using var store = new FileSentAlertStore(_root);
    await store.TryReserveAsync(NotifyPayload.AdhocScope, "k", T0.AddHours(-1), T0);

    (await DescribeAsync(Notify(), store)).Should().Contain("would suppress the notification");
  }

  [Fact]
  public async Task RecordOfAnotherQueue_DoesNotSuppress() {
    using var store = new FileSentAlertStore(_root);
    var token = await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0);
    await store.CommitAsync(token!, T0);

    (await DescribeAsync(Notify(), store)).Should().StartWith("would send a notification");
  }

  [Fact]
  public async Task NullStore_WouldSend() {
    (await DescribeAsync(Notify(), null)).Should().StartWith("would send a notification");
  }

  [Fact]
  public async Task StepWithoutDedupe_WouldSend() {
    using var store = new FileSentAlertStore(_root);

    (await DescribeAsync(Notify(dedupe: false), store)).Should().StartWith("would send a notification");
  }
}
