using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 170: the repository stores, omits, and checks <c>onTimeLimit</c>.</summary>
public sealed class FileSequenceRepositoryOnTimeLimitTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), $"gamebot-ontimelimit-{Guid.NewGuid():N}");

  public void Dispose() {
    try { Directory.Delete(_root, recursive: true); }
    catch (IOException) { /* best effort */ }
    GC.SuppressFinalize(this);
  }

  private string FileText(string id) => File.ReadAllText(Path.Combine(_root, "commands", "sequences", id + ".json"));

  [Fact]
  public async Task EndSurvivesARoundTrip() {
    var repo = new FileSequenceRepository(_root);
    await repo.CreateAsync(new CommandSequence { Id = "a", Name = "a", OnTimeLimit = "end" });

    (await repo.GetAsync("a"))!.OnTimeLimit.Should().Be("end");
    FileText("a").Should().Contain("\"onTimeLimit\"");
  }

  [Theory]
  [InlineData(null)]
  [InlineData("fail")]
  public async Task FailAndAbsentOmitTheMember(string? value) {
    var repo = new FileSequenceRepository(_root);
    await repo.CreateAsync(new CommandSequence { Id = "b", Name = "b", OnTimeLimit = value });

    FileText("b").Should().NotContain("onTimeLimit");
    (await repo.GetAsync("b"))!.OnTimeLimit.Should().BeNull();
  }

  [Fact]
  public async Task AnUnknownValueIsRejectedOnCreateAndUpdate() {
    var repo = new FileSequenceRepository(_root);
    var bad = new CommandSequence { Id = "c", Name = "c", OnTimeLimit = "stop" };

    var create = async () => await repo.CreateAsync(bad);
    (await create.Should().ThrowAsync<InvalidOperationException>()).WithMessage(SequenceTimeLimits.OnTimeLimitInvalidMessage);

    await repo.CreateAsync(new CommandSequence { Id = "c", Name = "c" });
    var update = async () => await repo.UpdateAsync(bad);
    (await update.Should().ThrowAsync<InvalidOperationException>()).WithMessage(SequenceTimeLimits.OnTimeLimitInvalidMessage);
  }

  [Fact]
  public async Task AnOldFileWithoutTheMemberLoadsAsFail() {
    var repo = new FileSequenceRepository(_root);
    await repo.CreateAsync(new CommandSequence { Id = "d", Name = "d" });

    var loaded = await repo.GetAsync("d");

    loaded!.OnTimeLimit.Should().BeNull("an absent member means fail");
  }
}
