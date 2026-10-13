#pragma warning disable CA2007 // Test code: no synchronization context.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>Feature 137: the sent-alert store (reserve, commit, release, expiry, prune, reload, delete).</summary>
public sealed class FileSentAlertStoreTests : IDisposable {
  private static readonly DateTimeOffset T0 = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
  private readonly string _root = Path.Combine(Path.GetTempPath(), "sent-alerts-" + Guid.NewGuid().ToString("N"));

  public void Dispose() {
    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
  }

  private FileSentAlertStore NewStore() => new(_root);

  [Fact]
  public async Task Reserve_Commit_SuppressesInWindow() {
    using var store = NewStore();
    var token = await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0);
    token.Should().NotBeNull();
    await store.CommitAsync(token!, T0);

    (await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0.AddMinutes(1))).Should().BeNull();
    (await store.TryReserveAsync("q1", "k", T0.AddMinutes(30), T0.AddMinutes(40))).Should().NotBeNull("the record is before the new window");
  }

  [Fact]
  public async Task Release_FreesTheSlot_AndKeepsNoRecord() {
    using var store = NewStore();
    var token = await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0);
    await store.ReleaseAsync(token!);

    (await store.WasSentSinceAsync("q1", "k", T0.AddHours(-1))).Should().BeNull();
    (await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0)).Should().NotBeNull();
  }

  [Fact]
  public async Task Reservation_DiesAfterFiveMinutes() {
    using var store = NewStore();
    (await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0)).Should().NotBeNull();

    (await store.HasLiveReservationAsync("q1", "k", T0.AddMinutes(4))).Should().BeTrue();
    (await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0.AddMinutes(4))).Should().BeNull();
    (await store.HasLiveReservationAsync("q1", "k", T0.AddMinutes(5))).Should().BeFalse();
    (await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0.AddMinutes(5))).Should().NotBeNull();
  }

  [Fact]
  public async Task HasLiveReservation_FalseAfterRelease() {
    using var store = NewStore();
    var token = await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0);
    await store.ReleaseAsync(token!);

    (await store.HasLiveReservationAsync("q1", "k", T0)).Should().BeFalse();
  }

  [Fact]
  public async Task Commit_PrunesRecordsOlderThan48Hours() {
    using var store = NewStore();
    var old = await store.TryReserveAsync("q1", "old", T0.AddDays(-5), T0.AddDays(-3));
    await store.CommitAsync(old!, T0.AddDays(-3));
    var fresh = await store.TryReserveAsync("q1", "new", T0.AddHours(-1), T0);
    await store.CommitAsync(fresh!, T0);

    (await store.WasSentSinceAsync("q1", "old", DateTimeOffset.MinValue)).Should().BeNull();
    (await store.WasSentSinceAsync("q1", "new", DateTimeOffset.MinValue)).Should().Be(T0);
  }

  [Fact]
  public async Task Records_SurviveARestart() {
    using (var store = NewStore()) {
      var token = await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0);
      await store.CommitAsync(token!, T0);
    }

    using var reloaded = NewStore();
    (await reloaded.WasSentSinceAsync("q1", "k", T0.AddHours(-1))).Should().Be(T0);
  }

  [Fact]
  public async Task DamagedFile_StartsEmpty() {
    Directory.CreateDirectory(Path.Combine(_root, FileSentAlertStore.FolderName));
    await File.WriteAllTextAsync(Path.Combine(_root, FileSentAlertStore.FolderName, FileSentAlertStore.FileName), "{ not json");

    using var store = NewStore();
    (await store.WasSentSinceAsync("q1", "k", DateTimeOffset.MinValue)).Should().BeNull();
  }

  [Fact]
  public async Task TwentyConcurrentReserves_GiveOneWinner() {
    using var store = NewStore();
    var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
      Task.Run(() => store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0))));

    results.Count(r => r is not null).Should().Be(1);
  }

  [Fact]
  public async Task DeleteQueue_RemovesOnlyThatQueue() {
    using var store = NewStore();
    var a = await store.TryReserveAsync("q1", "k", T0.AddHours(-1), T0);
    await store.CommitAsync(a!, T0);
    var b = await store.TryReserveAsync("q2", "k", T0.AddHours(-1), T0);
    await store.CommitAsync(b!, T0);
    await store.TryReserveAsync("q1", "live", T0.AddHours(-1), T0);

    await store.DeleteQueueAsync("q1");

    (await store.WasSentSinceAsync("q1", "k", DateTimeOffset.MinValue)).Should().BeNull();
    (await store.HasLiveReservationAsync("q1", "live", T0)).Should().BeFalse();
    (await store.WasSentSinceAsync("q2", "k", DateTimeOffset.MinValue)).Should().Be(T0);
    using var reloaded = NewStore();
    (await reloaded.WasSentSinceAsync("q1", "k", DateTimeOffset.MinValue)).Should().BeNull();
  }
}
