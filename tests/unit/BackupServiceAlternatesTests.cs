#pragma warning disable CA2007, CA1861, CA1859, CA2000, CA1849
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using GameBot.Service.Contracts.Backup;
using GameBot.Service.Services;
using GameBot.UnitTests.Images;
using Xunit;

namespace GameBot.UnitTests;

/// <summary>Feature 097 (FR-010): alternates travel with their images through backup and restore.</summary>
public sealed class BackupServiceAlternatesTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gamebot-backup-alts-" + Guid.NewGuid().ToString("N"));

  public void Dispose() {
    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
  }

  private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47 };

  private sealed class Commands : ICommandRepository {
    private readonly Dictionary<string, Command> _store = new(StringComparer.Ordinal);
    public Commands(params Command[] commands) { foreach (var c in commands) _store[c.Id] = c; }
    public Task<Command> AddAsync(Command c, CancellationToken ct = default) { _store[c.Id] = c; return Task.FromResult(c); }
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_store.Remove(id));
    public Task<Command?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(_store.TryGetValue(id, out var c) ? c : null);
    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Command>>(_store.Values.ToList());
    public Task<Command?> UpdateAsync(Command c, CancellationToken ct = default) => Task.FromResult<Command?>(c);
  }

  private sealed class Sequences : ISequenceRepository {
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(null);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence>());
    public Task<CommandSequence> CreateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<CommandSequence> UpdateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static Command TapCommand(string imageId) {
    var cmd = new Command { Id = "cmd1", Name = "Tap anchor" };
    cmd.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget(imageId, 0.85) }
    });
    return cmd;
  }

  private async Task<FileImageRepository> ImagesAsync(params string[] ids) {
    var repo = new FileImageRepository(Path.Combine(_root, Guid.NewGuid().ToString("N")));
    foreach (var id in ids) {
      using var ms = new MemoryStream(Png);
      await repo.SaveAsync(id, ms, "image/png", null, overwrite: true);
    }
    return repo;
  }

  private static async Task<MemoryStream> BackupAsync(BackupService svc) {
    var output = new MemoryStream();
    await svc.CreateBackupAsync(new BackupRequestDto { CommandIds = ["cmd1"] }, output, CancellationToken.None);
    output.Position = 0;
    return output;
  }

  [Fact]
  public async Task BackupIncludesAlternateImagesAndTheirList() {
    var images = await ImagesAsync("anchor", "anchor-n1", "anchor-n2");
    var alts = new ReferenceImageSetLoaderTests.MemoryAlternates();
    alts.SetAlternates("anchor", new[] { "anchor-n1", "gone", "anchor-n2" });
    var svc = new BackupService(new Commands(TapCommand("anchor")), new Sequences(), images, alts);

    using var output = await BackupAsync(svc);
    using var archive = new ZipArchive(output, ZipArchiveMode.Read);

    archive.GetEntry("images/anchor.png").Should().NotBeNull();
    archive.GetEntry("images/anchor-n1.png").Should().NotBeNull();
    archive.GetEntry("images/anchor-n2.png").Should().NotBeNull();
    var listEntry = archive.GetEntry("image-alternates/anchor.json");
    listEntry.Should().NotBeNull();
    using var doc = JsonDocument.Parse(listEntry!.Open());
    doc.RootElement.GetProperty("alternates").EnumerateArray().Select(e => e.GetString())
      .Should().Equal("anchor-n1", "gone", "anchor-n2");
  }

  [Fact]
  public async Task RestoreReappliesAlternatesKeepingOnlyStoredIds() {
    var source = await ImagesAsync("anchor", "anchor-n1", "anchor-n2");
    var sourceAlts = new ReferenceImageSetLoaderTests.MemoryAlternates();
    sourceAlts.SetAlternates("anchor", new[] { "anchor-n1", "gone", "anchor-n2" });
    using var archive = await BackupAsync(new BackupService(new Commands(TapCommand("anchor")), new Sequences(), source, sourceAlts));

    var target = await ImagesAsync();
    var targetAlts = new ReferenceImageSetLoaderTests.MemoryAlternates();
    var result = await new BackupService(new Commands(), new Sequences(), target, targetAlts).ApplyRestoreAsync(archive);

    result.RolledBack.Should().BeFalse(result.ErrorMessage);
    targetAlts.GetAlternates("anchor").Should().Equal("anchor-n1", "anchor-n2");
    (await target.ExistsAsync("anchor-n2")).Should().BeTrue();
  }

  [Fact]
  public async Task RestoringAnArchiveWithoutAlternatesLeavesExistingListsAlone() {
    var source = await ImagesAsync("anchor");
    using var archive = await BackupAsync(new BackupService(new Commands(TapCommand("anchor")), new Sequences(), source));

    var target = await ImagesAsync("anchor", "anchor-n1");
    var targetAlts = new ReferenceImageSetLoaderTests.MemoryAlternates();
    targetAlts.SetAlternates("anchor", new[] { "anchor-n1" });
    var result = await new BackupService(new Commands(), new Sequences(), target, targetAlts).ApplyRestoreAsync(archive);

    result.RolledBack.Should().BeFalse(result.ErrorMessage);
    targetAlts.GetAlternates("anchor").Should().Equal("anchor-n1");
  }

  [Fact]
  public async Task DryRunIgnoresAlternateListEntries() {
    var source = await ImagesAsync("anchor", "anchor-n1");
    var sourceAlts = new ReferenceImageSetLoaderTests.MemoryAlternates();
    sourceAlts.SetAlternates("anchor", new[] { "anchor-n1" });
    using var archive = await BackupAsync(new BackupService(new Commands(TapCommand("anchor")), new Sequences(), source, sourceAlts));

    var report = await new BackupService(new Commands(), new Sequences(), await ImagesAsync()).DryRunRestoreAsync(archive);

    report.TotalImages.Should().Be(2);
  }
}
