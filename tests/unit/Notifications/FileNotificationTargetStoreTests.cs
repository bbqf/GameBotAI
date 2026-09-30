using System;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using GameBot.Domain.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707

namespace GameBot.UnitTests.Notifications;

/// <summary>Feature 120: the file target store.</summary>
public sealed class FileNotificationTargetStoreTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gamebot-targets-" + Guid.NewGuid().ToString("N"));

  public FileNotificationTargetStoreTests() {
    Directory.CreateDirectory(_root);
  }

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); }
    catch (IOException) { /* best effort */ }
    GC.SuppressFinalize(this);
  }

  private string FilePath => Path.Combine(_root, "notifications", "targets.json");

  private static NotificationTarget Make(string name = "Phone", string chat = "1") {
    var target = new NotificationTarget { Type = "telegram", Name = name };
    target.Settings["chatId"] = chat;
    target.Settings["botToken"] = "123456:ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    return target;
  }

  [Fact]
  public void CreateSetsIdAndTimesAndSurvivesARestart() {
    var store = new FileNotificationTargetStore(_root);

    var created = store.Create(Make());

    created.Id.Should().NotBeNullOrEmpty();
    created.CreatedAt.Should().NotBeNull();
    created.UpdatedAt.Should().Be(created.CreatedAt);
    var second = new FileNotificationTargetStore(_root);
    var found = second.Find(created.Id);
    found.Should().NotBeNull();
    found!.Name.Should().Be("Phone");
    found.Settings["chatId"].Should().Be("1");
    found.Enabled.Should().BeTrue();
    second.List().Should().ContainSingle();
  }

  [Fact]
  public void UpdateReplacesTheTargetAndKeepsCreatedAt() {
    var store = new FileNotificationTargetStore(_root);
    var created = store.Create(Make());
    var change = created.Clone();
    change.Name = "Tablet";
    change.Enabled = false;

    var updated = store.Update(change);

    updated.Should().NotBeNull();
    updated!.Name.Should().Be("Tablet");
    updated.CreatedAt.Should().Be(created.CreatedAt);
    updated.UpdatedAt.Should().BeOnOrAfter(created.UpdatedAt!.Value);
    new FileNotificationTargetStore(_root).Find(created.Id)!.Enabled.Should().BeFalse();
  }

  [Fact]
  public void UpdateAndDeleteOfAnUnknownIdReturnNullAndFalse() {
    var store = new FileNotificationTargetStore(_root);
    var ghost = Make();
    ghost.Id = "ghost";

    store.Update(ghost).Should().BeNull();
    store.Delete("ghost").Should().BeFalse();
  }

  [Fact]
  public void DeleteRemovesTheTarget() {
    var store = new FileNotificationTargetStore(_root);
    var a = store.Create(Make("A"));
    store.Create(Make("B"));

    store.Delete(a.Id).Should().BeTrue();

    store.List().Select(t => t.Name).Should().Equal("B");
    new FileNotificationTargetStore(_root).List().Should().ContainSingle();
  }

  [Fact]
  public void AReturnedCopyCannotChangeTheStore() {
    var store = new FileNotificationTargetStore(_root);
    var created = store.Create(Make());

    created.Name = "Changed";
    created.Settings["chatId"] = "99";
    store.List()[0].Settings["chatId"] = "77";

    var stored = store.Find(created.Id)!;
    stored.Name.Should().Be("Phone");
    stored.Settings["chatId"].Should().Be("1");
  }

  [Fact]
  public void TheWriteLeavesNoTempFile() {
    var store = new FileNotificationTargetStore(_root);
    store.Create(Make());

    File.Exists(FilePath + ".tmp").Should().BeFalse();
    File.Exists(FilePath).Should().BeTrue();
  }

  [Fact]
  public void AHandEditApplies() {
    var store = new FileNotificationTargetStore(_root);
    store.Create(Make("A"));
    store.List().Should().ContainSingle();

    File.WriteAllText(FilePath, "[{\"id\":\"hand\",\"type\":\"telegram\",\"name\":\"Hand\",\"enabled\":true,\"settings\":{\"chatId\":\"5\"}},{\"id\":\"hand2\",\"type\":\"telegram\",\"name\":\"Hand 2\",\"settings\":{}}]");

    var list = store.List();
    list.Select(t => t.Name).Should().Equal("Hand", "Hand 2");
    store.Find("hand")!.Settings["chatId"].Should().Be("5");
  }

  [Theory]
  [InlineData("{ this is not json")]
  [InlineData("")]
  [InlineData("null")]
  public void ACorruptOrEmptyFileKeepsTheLastGoodList(string content) {
    var store = new FileNotificationTargetStore(_root);
    store.Create(Make("Good"));

    File.WriteAllText(FilePath, content);

    store.List().Select(t => t.Name).Should().Equal("Good");
  }

  [Fact]
  public void AStoreThatStartsOnACorruptFileHasAnEmptyListAndCanWriteAgain() {
    Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
    File.WriteAllText(FilePath, "garbage");
    var store = new FileNotificationTargetStore(_root);

    store.List().Should().BeEmpty();
    store.Create(Make("Fresh"));

    new FileNotificationTargetStore(_root).List().Select(t => t.Name).Should().Equal("Fresh");
  }

  [Fact]
  public void ADeletedFileIsAnEmptyList() {
    var store = new FileNotificationTargetStore(_root);
    store.Create(Make());

    File.Delete(FilePath);

    store.List().Should().BeEmpty();
  }

  [Fact]
  public void ConcurrentCreatesKeepAllTargets() {
    var store = new FileNotificationTargetStore(_root);

    var threads = Enumerable.Range(0, 8).Select(i => new Thread(() => store.Create(Make("T" + i)))).ToList();
    threads.ForEach(t => t.Start());
    threads.ForEach(t => t.Join());

    store.List().Should().HaveCount(8);
    new FileNotificationTargetStore(_root).List().Should().HaveCount(8);
  }

  [Fact]
  public void ABlankDataRootIsRefused() {
    Action act = () => GC.KeepAlive(new FileNotificationTargetStore(" "));

    act.Should().Throw<ArgumentException>();
  }
}
