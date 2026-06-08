using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using GameBot.Service.Contracts.Backup;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.UnitTests;

public sealed class BackupServiceTests {
  private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = true };

  // ────────────── helpers ──────────────

  private static Command MakeCommand(string id, string name, string? imgId = null) {
    var cmd = new Command { Id = id, Name = name };
    if (imgId is not null)
      cmd.Steps.Add(new CommandStep {
        Type = CommandStepType.PrimitiveTap,
        Order = 0,
        PrimitiveTap = new PrimitiveTapConfig {
          DetectionTarget = new DetectionTarget(imgId, 0.8, 0, 0, DetectionSelectionStrategy.HighestConfidence)
        }
      });
    return cmd;
  }

  private static CommandSequence MakeSequence(string id, string name, params string[] commandIds) {
    var seq = new CommandSequence { Id = id, Name = name };
    var steps = commandIds.Select((cid, i) => new SequenceStep { Order = i, CommandId = cid }).ToList();
    seq.SetSteps(steps);
    return seq;
  }

  private static Stream BuildZip(
    object manifest,
    IEnumerable<Command>? commands = null,
    IEnumerable<CommandSequence>? sequences = null,
    IEnumerable<(string Id, byte[] Data)>? images = null) {
    var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
      WriteJsonEntry(archive, "manifest.json", manifest);
      foreach (var cmd in commands ?? []) WriteJsonEntry(archive, $"commands/{cmd.Id}.json", cmd);
      foreach (var seq in sequences ?? []) WriteJsonEntry(archive, $"sequences/{seq.Id}.json", seq);
      foreach (var (id, data) in images ?? []) {
        var entry = archive.CreateEntry($"images/{id}.png");
        using var s = entry.Open();
        s.Write(data, 0, data.Length);
      }
    }
    ms.Position = 0;
    return ms;
  }

  private static void WriteJsonEntry(ZipArchive archive, string name, object value) {
    var entry = archive.CreateEntry(name);
    using var s = entry.Open();
    JsonSerializer.Serialize(s, value, JsonOpts);
  }

  // ────────────── spy repos ──────────────

  private sealed class StubCommandRepo : ICommandRepository {
    private readonly Dictionary<string, Command> _store;
    public List<Command> Added { get; } = new();
    public List<string> Deleted { get; } = new();

    public StubCommandRepo(params Command[] commands) =>
      _store = commands.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public Task<Command> AddAsync(Command c, CancellationToken ct = default) {
      Added.Add(c);
      _store[c.Id] = c;
      return Task.FromResult(c);
    }
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
      Deleted.Add(id);
      return Task.FromResult(_store.Remove(id));
    }
    public Task<Command?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult(_store.TryGetValue(id, out var c) ? c : null);
    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) =>
      Task.FromResult<IReadOnlyList<Command>>(_store.Values.ToList());
    public Task<Command?> UpdateAsync(Command c, CancellationToken ct = default) => Task.FromResult<Command?>(c);
  }

  private sealed class StubSequenceRepo : ISequenceRepository {
    private readonly Dictionary<string, CommandSequence> _store;
    public List<CommandSequence> Created { get; } = new();
    public List<string> Deleted { get; } = new();

    public StubSequenceRepo(params CommandSequence[] seqs) =>
      _store = seqs.ToDictionary(s => s.Id, StringComparer.Ordinal);

    public Task<CommandSequence?> GetAsync(string id) =>
      Task.FromResult(_store.TryGetValue(id, out var s) ? s : null);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() =>
      Task.FromResult<IReadOnlyList<CommandSequence>>(_store.Values.ToList());
    public Task<CommandSequence> CreateAsync(CommandSequence s) {
      Created.Add(s);
      _store[s.Id] = s;
      return Task.FromResult(s);
    }
    public Task<CommandSequence> UpdateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<bool> DeleteAsync(string id) {
      Deleted.Add(id);
      return Task.FromResult(_store.Remove(id));
    }
  }

  private sealed class StubImageRepo : IImageRepository {
    private readonly Dictionary<string, (byte[] Data, string ContentType)> _store;
    public List<(string Id, byte[] Data)> Saved { get; } = new();

    public StubImageRepo(params (string Id, byte[] Data, string ContentType)[] images) =>
      _store = images.ToDictionary(i => i.Id, i => (i.Data, i.ContentType), StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyCollection<string>> ListIdsAsync(CancellationToken ct = default) =>
      Task.FromResult<IReadOnlyCollection<string>>(_store.Keys.ToList());
    public Task<ImageAsset?> GetAsync(string id, CancellationToken ct = default) {
      if (!_store.TryGetValue(id, out var entry)) return Task.FromResult<ImageAsset?>(null);
      return Task.FromResult<ImageAsset?>(new ImageAsset(id, entry.ContentType, entry.Data.Length, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }
    public Task<Stream?> OpenReadAsync(string id, CancellationToken ct = default) {
      if (!_store.TryGetValue(id, out var entry)) return Task.FromResult<Stream?>(null);
      return Task.FromResult<Stream?>(new MemoryStream(entry.Data));
    }
    public async Task<ImageAsset> SaveAsync(string id, Stream content, string contentType, string? filename, bool overwrite, CancellationToken ct = default) {
      var ms = new MemoryStream();
      await content.CopyToAsync(ms, ct).ConfigureAwait(false);
      var data = ms.ToArray();
      Saved.Add((id, data));
      _store[id] = (data, contentType);
      return new ImageAsset(id, contentType, ms.Length, filename, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
      return Task.FromResult(_store.Remove(id));
    }
    public Task<bool> ExistsAsync(string id, CancellationToken ct = default) =>
      Task.FromResult(_store.ContainsKey(id));
  }

  // ────────────── CreateBackupAsync ──────────────

  [Fact]
  public async Task CreateBackupAsync_SelectedCommandAndImage_ProducesArchiveWithEntries() {
    var imgData = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
    var cmd = MakeCommand("cmd1", "Attack", "img-tap");
    var cmdRepo = new StubCommandRepo(cmd);
    var seqRepo = new StubSequenceRepo();
    var imgRepo = new StubImageRepo(("img-tap", imgData, "image/png"));
    var svc = new BackupService(cmdRepo, seqRepo, imgRepo);

    var output = new MemoryStream();
    await svc.CreateBackupAsync(new BackupRequestDto { CommandIds = ["cmd1"] }, output, CancellationToken.None).ConfigureAwait(false);

    output.Position = 0;
    using var archive = new ZipArchive(output, ZipArchiveMode.Read);
    archive.GetEntry("manifest.json").Should().NotBeNull();
    archive.GetEntry("commands/cmd1.json").Should().NotBeNull();
    archive.GetEntry("images/img-tap.png").Should().NotBeNull();
  }

  [Fact]
  public async Task CreateBackupAsync_SequenceTransitivelyIncludesReferencedCommand() {
    var cmd = MakeCommand("cmd1", "Attack");
    var seq = MakeSequence("seq1", "MySeq", "cmd1");
    var cmdRepo = new StubCommandRepo(cmd);
    var seqRepo = new StubSequenceRepo(seq);
    var imgRepo = new StubImageRepo();
    var svc = new BackupService(cmdRepo, seqRepo, imgRepo);

    var output = new MemoryStream();
    await svc.CreateBackupAsync(new BackupRequestDto { SequenceIds = ["seq1"] }, output, CancellationToken.None).ConfigureAwait(false);

    output.Position = 0;
    using var archive = new ZipArchive(output, ZipArchiveMode.Read);
    archive.GetEntry("commands/cmd1.json").Should().NotBeNull("sequence references it transitively");
    archive.GetEntry("sequences/seq1.json").Should().NotBeNull();
  }

  [Fact(DisplayName = "Sequence with per-step break condition round-trips through backup JSON options")]
  public void BackupJsonOptions_SequenceWithBreakConditionRoundTripsWithoutDuplicateTypeKey() {
    var seq = new CommandSequence { Id = "seq1", Name = "LoopSeq" };
    var loopStep = new SequenceStep {
      Order = 0,
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 3 },
      Body = new List<SequenceStep> {
        new SequenceStep {
          Order = 0,
          StepType = SequenceStepType.Break,
          BreakCondition = new ImageVisibleStepCondition { ImageId = "img1", MinSimilarity = 0.9 }
        }
      }
    };
    seq.SetSteps([loopStep]);

    var json = JsonSerializer.Serialize(seq, JsonOpts);
    var loaded = JsonSerializer.Deserialize<CommandSequence>(json, JsonOpts);

    loaded.Should().NotBeNull();
    var breakCondition = loaded!.Steps[0].Body[0].BreakCondition;
    breakCondition.Should().BeOfType<ImageVisibleStepCondition>();
    ((ImageVisibleStepCondition)breakCondition!).ImageId.Should().Be("img1");
  }

  [Fact]
  public async Task CreateBackupAsync_DeduplicatesCommandsAndImages() {
    var imgData = new byte[] { 1, 2, 3 };
    var cmd = MakeCommand("cmd1", "Attack", "img-shared");
    var seq = MakeSequence("seq1", "MySeq", "cmd1");
    var cmdRepo = new StubCommandRepo(cmd);
    var seqRepo = new StubSequenceRepo(seq);
    var imgRepo = new StubImageRepo(("img-shared", imgData, "image/png"));
    var svc = new BackupService(cmdRepo, seqRepo, imgRepo);

    var output = new MemoryStream();
    await svc.CreateBackupAsync(new BackupRequestDto { CommandIds = ["cmd1"], SequenceIds = ["seq1"] }, output, CancellationToken.None).ConfigureAwait(false);

    output.Position = 0;
    using var archive = new ZipArchive(output, ZipArchiveMode.Read);
    archive.Entries.Count(e => e.FullName.StartsWith("commands/", StringComparison.Ordinal)).Should().Be(1);
    archive.Entries.Count(e => e.FullName.StartsWith("images/", StringComparison.Ordinal)).Should().Be(1);
  }

  // ────────────── DryRunRestoreAsync ──────────────

  [Fact]
  public async Task DryRunRestoreAsync_NoConflicts_ReturnsEmptyReport() {
    var manifest = new { version = "1.0", createdAt = DateTimeOffset.UtcNow, commandCount = 1, sequenceCount = 0, imageCount = 0 };
    var archiveCmd = MakeCommand("cmd1", "NewCmd");
    using var stream = BuildZip(manifest, commands: [archiveCmd]);

    var cmdRepo = new StubCommandRepo();
    var seqRepo = new StubSequenceRepo();
    var imgRepo = new StubImageRepo();
    var svc = new BackupService(cmdRepo, seqRepo, imgRepo);

    var report = await svc.DryRunRestoreAsync(stream, CancellationToken.None).ConfigureAwait(false);

    report.HasConflicts.Should().BeFalse();
    report.TotalCommands.Should().Be(1);
  }

  [Fact]
  public async Task DryRunRestoreAsync_NameConflict_ReturnsConflictReport() {
    var manifest = new { version = "1.0", createdAt = DateTimeOffset.UtcNow, commandCount = 1, sequenceCount = 0, imageCount = 0 };
    var archiveCmd = MakeCommand("cmd-new", "Attack");
    using var stream = BuildZip(manifest, commands: [archiveCmd]);

    var existingCmd = MakeCommand("cmd-old", "Attack");
    var cmdRepo = new StubCommandRepo(existingCmd);
    var seqRepo = new StubSequenceRepo();
    var imgRepo = new StubImageRepo();
    var svc = new BackupService(cmdRepo, seqRepo, imgRepo);

    var report = await svc.DryRunRestoreAsync(stream, CancellationToken.None).ConfigureAwait(false);

    report.HasConflicts.Should().BeTrue();
    report.ConflictingCommandNames.Should().Contain("Attack");
  }

  [Fact]
  public async Task DryRunRestoreAsync_MissingManifest_ThrowsBackupFormatException() {
    var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
      var entry = archive.CreateEntry("commands/cmd1.json");
      using var s = entry.Open();
      JsonSerializer.Serialize(s, MakeCommand("cmd1", "C"), JsonOpts);
    }
    ms.Position = 0;

    var svc = new BackupService(new StubCommandRepo(), new StubSequenceRepo(), new StubImageRepo());
    var act = () => svc.DryRunRestoreAsync(ms, CancellationToken.None);
    await act.Should().ThrowAsync<BackupFormatException>().ConfigureAwait(false);
  }

  [Fact]
  public async Task DryRunRestoreAsync_MissingImageInArchive_ThrowsBackupFormatException() {
    var manifest = new { version = "1.0", createdAt = DateTimeOffset.UtcNow, commandCount = 1, sequenceCount = 0, imageCount = 0 };
    var cmd = MakeCommand("cmd1", "C", "missing-img");
    using var stream = BuildZip(manifest, commands: [cmd]);

    var svc = new BackupService(new StubCommandRepo(), new StubSequenceRepo(), new StubImageRepo());
    var act = () => svc.DryRunRestoreAsync(stream, CancellationToken.None);
    await act.Should().ThrowAsync<BackupFormatException>().WithMessage("*missing-img*").ConfigureAwait(false);
  }

  // ────────────── ApplyRestoreAsync (no-conflict) ──────────────

  [Fact]
  public async Task ApplyRestoreAsync_NoConflict_CreatesAllObjectsWithoutDelete() {
    var manifest = new { version = "1.0", createdAt = DateTimeOffset.UtcNow, commandCount = 1, sequenceCount = 1, imageCount = 0 };
    var archiveCmd = MakeCommand("cmd1", "Attack");
    var archiveSeq = MakeSequence("seq1", "MySeq");
    using var stream = BuildZip(manifest, commands: [archiveCmd], sequences: [archiveSeq]);

    var cmdRepo = new StubCommandRepo();
    var seqRepo = new StubSequenceRepo();
    var imgRepo = new StubImageRepo();
    var svc = new BackupService(cmdRepo, seqRepo, imgRepo);

    var result = await svc.ApplyRestoreAsync(stream, CancellationToken.None).ConfigureAwait(false);

    result.RolledBack.Should().BeFalse();
    result.RestoredCommands.Should().Be(1);
    result.RestoredSequences.Should().Be(1);
    cmdRepo.Added.Should().HaveCount(1);
    cmdRepo.Deleted.Should().BeEmpty();
    seqRepo.Created.Should().HaveCount(1);
    seqRepo.Deleted.Should().BeEmpty();
  }

  // ────────────── ApplyRestoreAsync (conflict + rollback) ──────────────

  [Fact]
  public async Task ApplyRestoreAsync_WithConflict_DeletesOldAndCreatesNew() {
    var manifest = new { version = "1.0", createdAt = DateTimeOffset.UtcNow, commandCount = 1, sequenceCount = 0, imageCount = 0 };
    var archiveCmd = MakeCommand("cmd-new", "Attack");
    using var stream = BuildZip(manifest, commands: [archiveCmd]);

    var existingCmd = MakeCommand("cmd-old", "Attack");
    var cmdRepo = new StubCommandRepo(existingCmd);
    var seqRepo = new StubSequenceRepo();
    var imgRepo = new StubImageRepo();
    var svc = new BackupService(cmdRepo, seqRepo, imgRepo);

    var result = await svc.ApplyRestoreAsync(stream, CancellationToken.None).ConfigureAwait(false);

    result.RolledBack.Should().BeFalse();
    cmdRepo.Deleted.Should().Contain("cmd-old");
    cmdRepo.Added.Any(c => c.Name == "Attack").Should().BeTrue();
  }

  [Fact]
  public async Task ApplyRestoreAsync_MissingImageInArchive_ThrowsBackupFormatException() {
    var manifest = new { version = "1.0", createdAt = DateTimeOffset.UtcNow, commandCount = 1, sequenceCount = 0, imageCount = 0 };
    var cmd = MakeCommand("cmd1", "C", "missing-img");
    using var stream = BuildZip(manifest, commands: [cmd]);

    var svc = new BackupService(new StubCommandRepo(), new StubSequenceRepo(), new StubImageRepo());
    var act = () => svc.ApplyRestoreAsync(stream, CancellationToken.None);
    await act.Should().ThrowAsync<BackupFormatException>().WithMessage("*missing-img*").ConfigureAwait(false);
  }

  // ────────────── ApplyRestoreAsync rollback on apply failure ──────────────

  [Fact]
  public async Task ApplyRestoreAsync_ApplyFails_RollsBackAndReturnsRolledBackTrue() {
    var manifest = new { version = "1.0", createdAt = DateTimeOffset.UtcNow, commandCount = 1, sequenceCount = 0, imageCount = 0 };
    var archiveCmd = MakeCommand("cmd-new", "Attack");
    using var stream = BuildZip(manifest, commands: [archiveCmd]);

    var existingCmd = MakeCommand("cmd-old", "Attack");
    var faultyCmdRepo = new FaultyAfterDeleteCommandRepo(existingCmd);
    var svc = new BackupService(faultyCmdRepo, new StubSequenceRepo(), new StubImageRepo());

    var result = await svc.ApplyRestoreAsync(stream, CancellationToken.None).ConfigureAwait(false);

    result.RolledBack.Should().BeTrue();
    // Rollback should have re-added the original command
    faultyCmdRepo.RollbackAddCalled.Should().BeTrue();
  }

  // Throws on AddAsync for new objects (after delete), but allows rollback adds
  private sealed class FaultyAfterDeleteCommandRepo : ICommandRepository {
    private readonly Command _original;
    private bool _deleted;
    public bool RollbackAddCalled { get; private set; }

    public FaultyAfterDeleteCommandRepo(Command original) => _original = original;

    public Task<Command> AddAsync(Command c, CancellationToken ct = default) {
      if (_deleted) {
        // First call after delete = new object from archive → throw to trigger rollback
        if (!RollbackAddCalled && string.Equals(c.Id, _original.Id, StringComparison.Ordinal) == false) {
          throw new InvalidOperationException("Simulated apply failure.");
        }
        // Second call = rollback of original
        RollbackAddCalled = true;
        return Task.FromResult(c);
      }
      return Task.FromResult(c);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
      _deleted = true;
      return Task.FromResult(true);
    }

    public Task<Command?> GetAsync(string id, CancellationToken ct = default) =>
      Task.FromResult<Command?>(_original.Id == id ? _original : null);

    public Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) =>
      Task.FromResult<IReadOnlyList<Command>>(new List<Command> { _original });

    public Task<Command?> UpdateAsync(Command c, CancellationToken ct = default) => Task.FromResult<Command?>(c);
  }
}
