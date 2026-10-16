using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Gamepacks;

/// <summary>How the apply treats the kept previous version.</summary>
public enum GamepackPreviousHandling {
  /// <summary>Keep the record and the file as they are (new install, or the same version again).</summary>
  Keep,

  /// <summary>Capture the live content of the current version as the new previous version (FR-022).</summary>
  Capture,

  /// <summary>Clear the previous version (after a rollback, FR-011).</summary>
  Clear
}

/// <summary>The status of an apply.</summary>
public enum GamepackApplyStatus {
  /// <summary>All writes worked.</summary>
  Applied,

  /// <summary>A write failed and the restore brought back the state from before the request.</summary>
  Failed,

  /// <summary>A write failed and the restore also failed (FR-021). The state can be partly restored.</summary>
  PartialRestore
}

/// <summary>The result of an apply.</summary>
public sealed class GamepackApplyOutcome {
  /// <summary>The status.</summary>
  public GamepackApplyStatus Status { get; init; }

  /// <summary>The error that stopped the apply, or null.</summary>
  public Exception? Error { get; init; }

  /// <summary>The keys of the objects whose restore failed.</summary>
  public IReadOnlyList<string> FailedRestoreKeys { get; init; } = Array.Empty<string>();

  /// <summary>The number of created objects.</summary>
  public int Created { get; init; }

  /// <summary>The number of updated objects.</summary>
  public int Updated { get; init; }

  /// <summary>The number of removed objects.</summary>
  public int Removed { get; init; }
}

/// <summary>
/// The one apply path for an install and a rollback (feature 143). It takes an undo snapshot of every object it
/// touches, writes the objects, then the previous-version file, then the registry. On any exception it restores the
/// snapshot in reverse order (FR-003). If the restore fails, it logs the failed object and reports
/// <see cref="GamepackApplyStatus.PartialRestore"/> (FR-021).
/// </summary>
public sealed partial class GamepackApplier {
  private readonly ISequenceRepository _sequences;
  private readonly ICommandRepository _commands;
  private readonly IImageRepository _images;
  private readonly IGamepackRegistryStore _store;
  private readonly IGamepackWriteGate _gate;
  private readonly ILogger _logger;
  private readonly TimeProvider _time;

  /// <summary>Creates the applier.</summary>
  /// <param name="sequences">The sequence store.</param>
  /// <param name="commands">The command store.</param>
  /// <param name="images">The image store.</param>
  /// <param name="store">The registry store.</param>
  /// <param name="gate">The write gate.</param>
  /// <param name="logger">The logger, or null.</param>
  /// <param name="timeProvider">The clock, or null.</param>
  public GamepackApplier(
      ISequenceRepository sequences,
      ICommandRepository commands,
      IImageRepository images,
      IGamepackRegistryStore store,
      IGamepackWriteGate gate,
      ILogger<GamepackApplier>? logger = null,
      TimeProvider? timeProvider = null) {
    _sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
    _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    _images = images ?? throw new ArgumentNullException(nameof(images));
    _store = store ?? throw new ArgumentNullException(nameof(store));
    _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    _logger = (ILogger?)logger ?? NullLogger.Instance;
    _time = timeProvider ?? TimeProvider.System;
  }

  /// <summary>Applies a plan.</summary>
  /// <param name="plan">The plan from the validator.</param>
  /// <param name="previous">How to treat the previous version.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<GamepackApplyOutcome> ApplyAsync(GamepackPlan plan, GamepackPreviousHandling previous, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(plan);
    var packName = plan.Manifest.Name!;
    var packKey = GamepackNameRules.Key(packName);
    var undo = new List<Snapshot>();
    GamepackRegistry registryBefore;
    bool registryExistedBefore;
    string? previousFileBefore;
    string? capturedText = null;
    PreviousVersion? capturedRecord = null;
    InstalledGamepack? oldRecord;

    try {
      registryExistedBefore = _store.RegistryExists;
      registryBefore = _store.Load();
      registryBefore.Gamepacks.TryGetValue(packKey, out oldRecord);
      previousFileBefore = _store.ReadPrevious(packName);
      if (previous == GamepackPreviousHandling.Capture && oldRecord is not null) {
        (capturedText, capturedRecord) = await CaptureLiveAsync(oldRecord, registryBefore, ct).ConfigureAwait(false);
      }

      foreach (var write in plan.Writes) {
        undo.Add(await TakeSnapshotAsync(write.Type, write.Key, write.FinalId, ct).ConfigureAwait(false));
      }

      foreach (var removal in plan.Removals) {
        undo.Add(await TakeSnapshotAsync(removal.Type, removal.Key, removal.Id, ct).ConfigureAwait(false));
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      // Nothing was written yet.
      return new GamepackApplyOutcome { Status = GamepackApplyStatus.Failed, Error = ex };
    }

    var touched = new List<Snapshot>();
    var registryTouched = false;
    var previousTouched = false;
    try {
      foreach (var write in plan.Writes) {
        ct.ThrowIfCancellationRequested();
        var snapshot = undo.First(s => s.Matches(write.Type, write.FinalId));
        touched.Add(snapshot);
        await WriteObjectAsync(write, snapshot, ct).ConfigureAwait(false);
      }

      foreach (var removal in plan.Removals) {
        ct.ThrowIfCancellationRequested();
        var snapshot = undo.First(s => s.Matches(removal.Type, removal.Id));
        touched.Add(snapshot);
        _gate.BeforeWrite(GamepackWriteOperations.Delete, removal.Key);
        await DeleteObjectAsync(removal.Type, removal.Id).ConfigureAwait(false);
      }

      previousTouched = true;
      WritePreviousFile(packName, previous, capturedText);
      registryTouched = true;
      _gate.BeforeWrite(GamepackWriteOperations.Registry, packName);
      _store.Update(registry => ApplyToRegistry(registry, plan, previous, oldRecord, capturedRecord));
    }
    catch (Exception ex) {
      var failed = await RestoreAsync(registryTouched, registryBefore, registryExistedBefore, previousTouched, packName, previousFileBefore, touched).ConfigureAwait(false);
      return new GamepackApplyOutcome {
        Status = failed.Count == 0 ? GamepackApplyStatus.Failed : GamepackApplyStatus.PartialRestore,
        Error = ex,
        FailedRestoreKeys = failed
      };
    }

    return new GamepackApplyOutcome {
      Status = GamepackApplyStatus.Applied,
      Created = plan.Created,
      Updated = plan.Updated,
      Removed = plan.Removed
    };
  }

  // ---- writes ----

  private void WritePreviousFile(string packName, GamepackPreviousHandling previous, string? capturedText) {
    switch (previous) {
      case GamepackPreviousHandling.Capture when capturedText is not null:
        _gate.BeforeWrite(GamepackWriteOperations.PreviousFile, packName);
        _store.WritePrevious(packName, capturedText);
        break;
      case GamepackPreviousHandling.Clear:
        _gate.BeforeWrite(GamepackWriteOperations.PreviousFile, packName);
        _store.DeletePrevious(packName);
        break;
    }
  }

  private async Task WriteObjectAsync(PlannedObject write, Snapshot snapshot, CancellationToken ct) {
    _gate.BeforeWrite(write.IsUpdate ? GamepackWriteOperations.Update : GamepackWriteOperations.Create, write.Key);
    var now = _time.GetUtcNow();
    switch (write.Type) {
      case GamepackObjectTypes.Sequence: {
        var sequence = write.Sequence!;
        sequence.CreatedAt = snapshot.Sequence?.CreatedAt ?? now;
        sequence.UpdatedAt = now;
        if (write.IsUpdate) {
          await _sequences.UpdateAsync(sequence).ConfigureAwait(false);
        }
        else {
          await _sequences.CreateAsync(sequence).ConfigureAwait(false);
        }

        break;
      }

      case GamepackObjectTypes.Command: {
        var command = write.Command!;
        if (write.IsUpdate) {
          var updated = await _commands.UpdateAsync(command, ct).ConfigureAwait(false);
          if (updated is null) {
            throw new InvalidOperationException($"The command '{write.Name}' could not be updated.");
          }
        }
        else {
          await _commands.AddAsync(command, ct).ConfigureAwait(false);
        }

        break;
      }

      case GamepackObjectTypes.Image: {
        using var stream = new MemoryStream(write.ImageBytes!, writable: false);
        await _images.SaveAsync(write.FinalId, stream, write.Image!.ContentType!, write.Image.Filename, overwrite: true, ct).ConfigureAwait(false);
        break;
      }
    }
  }

  private async Task DeleteObjectAsync(string type, string id) {
    switch (type) {
      case GamepackObjectTypes.Sequence:
        await _sequences.DeleteAsync(id).ConfigureAwait(false);
        break;
      case GamepackObjectTypes.Command:
        await _commands.DeleteAsync(id).ConfigureAwait(false);
        break;
      case GamepackObjectTypes.Image:
        await _images.DeleteAsync(id).ConfigureAwait(false);
        break;
    }
  }

  private void ApplyToRegistry(GamepackRegistry registry, GamepackPlan plan, GamepackPreviousHandling previous, InstalledGamepack? oldRecord, PreviousVersion? captured) {
    var manifest = plan.Manifest;
    var name = manifest.Name!;
    var packKey = GamepackNameRules.Key(name);

    foreach (var key in registry.Owners.Where(p => GamepackNameRules.Comparer.Equals(p.Value.Gamepack, name)).Select(p => p.Key).ToList()) {
      registry.Owners.Remove(key);
    }

    foreach (var write in plan.Writes) {
      registry.Owners[write.Key] = new OwnerEntry { Gamepack = name, Version = manifest.Version!, Id = write.FinalId };
    }

    var record = new InstalledGamepack {
      Name = name,
      Version = manifest.Version!,
      Changelog = manifest.Changelog ?? string.Empty,
      RequiredApiMajor = manifest.RequiredApiMajor,
      InstalledAtUtc = _time.GetUtcNow(),
      Objects = plan.Writes.Select(w => new GamepackObjectRef { Type = w.Type, Name = w.Name, Id = w.FinalId }).ToList(),
      Previous = previous switch {
        GamepackPreviousHandling.Capture => captured ?? oldRecord?.Previous,
        GamepackPreviousHandling.Keep => oldRecord?.Previous,
        _ => null
      }
    };
    registry.Gamepacks[packKey] = record;
  }

  // ---- capture of live content ----

  /// <summary>Reads the live content of an installed gamepack as a gamepack file text (feature 148). It changes nothing.</summary>
  /// <param name="current">The registry record of the installed gamepack.</param>
  /// <param name="registry">The registry that holds the owner entries.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<string> CaptureLiveTextAsync(InstalledGamepack current, GamepackRegistry registry, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(current);
    ArgumentNullException.ThrowIfNull(registry);
    var (text, _) = await CaptureLiveAsync(current, registry, ct).ConfigureAwait(false);
    return text;
  }

  private async Task<(string Text, PreviousVersion Record)> CaptureLiveAsync(InstalledGamepack current, GamepackRegistry registry, CancellationToken ct) {
    var sequences = new List<CommandSequence>();
    var commands = new List<Command>();
    var images = new List<GamepackImage>();
    var objects = new List<GamepackObjectRef>();

    foreach (var pair in registry.Owners.Where(p => GamepackNameRules.Comparer.Equals(p.Value.Gamepack, current.Name))) {
      var type = GamepackRegistry.TypeOfKey(pair.Key);
      var id = pair.Value.Id;
      switch (type) {
        case GamepackObjectTypes.Sequence: {
          var sequence = await _sequences.GetAsync(id).ConfigureAwait(false);
          if (sequence is not null) {
            sequences.Add(sequence);
            objects.Add(new GamepackObjectRef { Type = type, Name = sequence.Name, Id = id });
          }

          break;
        }

        case GamepackObjectTypes.Command: {
          var command = await _commands.GetAsync(id, ct).ConfigureAwait(false);
          if (command is not null) {
            commands.Add(command);
            objects.Add(new GamepackObjectRef { Type = type, Name = command.Name, Id = id });
          }

          break;
        }

        case GamepackObjectTypes.Image: {
          var image = await ReadImageAsync(id, ct).ConfigureAwait(false);
          if (image is not null) {
            images.Add(image);
            objects.Add(new GamepackObjectRef { Type = type, Name = id, Id = id });
          }

          break;
        }
      }
    }

    var file = new GamepackFile {
      Manifest = new GamepackManifest {
        Name = current.Name,
        Version = current.Version,
        Changelog = current.Changelog,
        RequiredApiMajor = current.RequiredApiMajor
      },
      Sequences = sequences.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList(),
      Commands = commands.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
      Images = images.OrderBy(i => i.Id, StringComparer.OrdinalIgnoreCase).ToList()
    };

    var record = new PreviousVersion {
      Version = current.Version,
      Changelog = current.Changelog,
      RequiredApiMajor = current.RequiredApiMajor,
      InstalledAtUtc = current.InstalledAtUtc,
      Objects = objects
    };
    return (GamepackFileSerializer.Serialize(file), record);
  }

  private async Task<GamepackImage?> ReadImageAsync(string id, CancellationToken ct) {
    var asset = await _images.GetAsync(id, ct).ConfigureAwait(false);
    if (asset is null) {
      return null;
    }

    var stream = await _images.OpenReadAsync(id, ct).ConfigureAwait(false);
    if (stream is null) {
      return null;
    }

    await using (stream.ConfigureAwait(false)) {
      using var buffer = new MemoryStream();
      await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
      return new GamepackImage {
        Id = id,
        ContentType = asset.ContentType,
        Filename = asset.Filename,
        DataBase64 = Convert.ToBase64String(buffer.ToArray())
      };
    }
  }

  // ---- undo ----

  private sealed class Snapshot {
    public required string Type { get; init; }
    public required string Key { get; init; }
    public required string Id { get; init; }
    public bool Existed { get; set; }
    public CommandSequence? Sequence { get; set; }
    public Command? Command { get; set; }
    public byte[]? ImageBytes { get; set; }
    public string? ImageContentType { get; set; }
    public string? ImageFilename { get; set; }

    public bool Matches(string type, string id) =>
      string.Equals(Type, type, StringComparison.Ordinal) && string.Equals(Id, id, StringComparison.OrdinalIgnoreCase);
  }

  private async Task<Snapshot> TakeSnapshotAsync(string type, string key, string id, CancellationToken ct) {
    var snapshot = new Snapshot { Type = type, Key = key, Id = id };
    switch (type) {
      case GamepackObjectTypes.Sequence:
        snapshot.Sequence = await _sequences.GetAsync(id).ConfigureAwait(false);
        snapshot.Existed = snapshot.Sequence is not null;
        break;
      case GamepackObjectTypes.Command:
        snapshot.Command = await _commands.GetAsync(id, ct).ConfigureAwait(false);
        snapshot.Existed = snapshot.Command is not null;
        break;
      case GamepackObjectTypes.Image: {
        var image = await ReadImageAsync(id, ct).ConfigureAwait(false);
        snapshot.Existed = image is not null;
        if (image is not null) {
          snapshot.ImageBytes = Convert.FromBase64String(image.DataBase64!);
          snapshot.ImageContentType = image.ContentType;
          snapshot.ImageFilename = image.Filename;
        }

        break;
      }
    }

    return snapshot;
  }

  private async Task<IReadOnlyList<string>> RestoreAsync(
      bool registryTouched,
      GamepackRegistry registryBefore,
      bool registryExistedBefore,
      bool previousTouched,
      string packName,
      string? previousFileBefore,
      List<Snapshot> touched) {
    var failed = new List<string>();

    if (registryTouched) {
      try {
        _gate.BeforeWrite(GamepackWriteOperations.Restore, "registry");
        if (registryExistedBefore) {
          _store.Save(registryBefore);
        }
        else {
          _store.DeleteRegistry();
        }
      }
      catch (Exception ex) {
        failed.Add("registry");
        LogRestoreFailed(_logger, "registry", ex);
      }
    }

    if (previousTouched) {
      try {
        _gate.BeforeWrite(GamepackWriteOperations.Restore, "previous-file");
        if (previousFileBefore is null) {
          _store.DeletePrevious(packName);
        }
        else {
          _store.WritePrevious(packName, previousFileBefore);
        }
      }
      catch (Exception ex) {
        failed.Add("previous-file");
        LogRestoreFailed(_logger, "previous-file", ex);
      }
    }

    for (var i = touched.Count - 1; i >= 0; i--) {
      var snapshot = touched[i];
      try {
        _gate.BeforeWrite(GamepackWriteOperations.Restore, snapshot.Key);
        await RestoreObjectAsync(snapshot).ConfigureAwait(false);
      }
      catch (Exception ex) {
        failed.Add(snapshot.Key);
        LogRestoreFailed(_logger, snapshot.Key, ex);
      }
    }

    return failed;
  }

  private async Task RestoreObjectAsync(Snapshot snapshot) {
    if (!snapshot.Existed) {
      await DeleteObjectAsync(snapshot.Type, snapshot.Id).ConfigureAwait(false);
      return;
    }

    switch (snapshot.Type) {
      case GamepackObjectTypes.Sequence:
        await _sequences.CreateAsync(snapshot.Sequence!).ConfigureAwait(false);
        break;
      case GamepackObjectTypes.Command:
        await _commands.AddAsync(snapshot.Command!).ConfigureAwait(false);
        break;
      case GamepackObjectTypes.Image: {
        using var stream = new MemoryStream(snapshot.ImageBytes!, writable: false);
        await _images.SaveAsync(snapshot.Id, stream, snapshot.ImageContentType ?? "image/png", snapshot.ImageFilename, overwrite: true).ConfigureAwait(false);
        break;
      }
    }
  }

  [LoggerMessage(EventId = 1430, Level = LogLevel.Error, Message = "Gamepack restore failed for object {ObjectKey}. The state can be partly restored.")]
  private static partial void LogRestoreFailed(ILogger logger, string objectKey, Exception exception);
}
