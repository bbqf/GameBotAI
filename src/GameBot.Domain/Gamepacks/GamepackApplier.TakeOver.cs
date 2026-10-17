using System.Text.Json;
using GameBot.Domain.Commands;
using Microsoft.Extensions.Logging;

namespace GameBot.Domain.Gamepacks;

/// <summary>The take-over part of the applier (feature 149): the backup before the writes, and the restore of a backup.</summary>
public sealed partial class GamepackApplier {
  // ---- backup before the writes ----

  private static Snapshot? PreloadedSnapshot(PlannedObject write) {
    if (!write.IsTakeOver) {
      return null;
    }

    return write.Type switch {
      GamepackObjectTypes.Sequence when write.LiveSequence is not null =>
        new Snapshot { Type = write.Type, Key = write.Key, Id = write.FinalId, Existed = true, Sequence = write.LiveSequence },
      GamepackObjectTypes.Command when write.LiveCommand is not null =>
        new Snapshot { Type = write.Type, Key = write.Key, Id = write.FinalId, Existed = true, Command = write.LiveCommand },
      _ => null
    };
  }

  private string WriteTakeOverBackup(GamepackPlan plan, List<Snapshot> undo) {
    if (_takeOverBackups is null) {
      throw new InvalidOperationException("The plan has a take-over, but the applier has no take-over backup store.");
    }

    var backup = new TakeOverBackup {
      CreatedAtUtc = _time.GetUtcNow(),
      Gamepack = plan.Manifest.Name!,
      Version = plan.Manifest.Version!
    };
    foreach (var write in plan.Writes.Where(w => w.IsTakeOver)) {
      var snapshot = undo.First(s => s.Matches(write.Type, write.FinalId));
      if (!snapshot.Existed) {
        throw new InvalidOperationException($"The {write.Type} '{write.Name}' to take over does not exist any more.");
      }

      backup.Entries.Add(ToBackupEntry(write, snapshot));
    }

    _gate.BeforeWrite(GamepackWriteOperations.TakeOverBackup, plan.Manifest.Name!);
    return _takeOverBackups.Write(backup);
  }

  private static TakeOverBackupEntry ToBackupEntry(PlannedObject write, Snapshot snapshot) {
    var entry = new TakeOverBackupEntry { Type = write.Type, Name = write.Name, ObjectId = snapshot.Id };
    switch (write.Type) {
      case GamepackObjectTypes.Sequence:
        entry.Sequence = JsonSerializer.SerializeToElement(snapshot.Sequence, GamepackFileSerializer.SequenceJsonOptions);
        entry.PostHash = write.PostHash ?? GamepackContentComparer.Hash(write.Sequence!);
        break;
      case GamepackObjectTypes.Command:
        entry.Command = JsonSerializer.SerializeToElement(snapshot.Command, GamepackFileSerializer.CommandJsonOptions);
        entry.PostHash = write.PostHash ?? GamepackContentComparer.Hash(write.Command!);
        break;
      case GamepackObjectTypes.Image:
        entry.Image = new TakeOverBackupImage {
          ContentType = snapshot.ImageContentType ?? "image/png",
          Filename = snapshot.ImageFilename,
          DataBase64 = Convert.ToBase64String(snapshot.ImageBytes!),
          Alternates = snapshot.ImageAlternates is { Count: > 0 } ? snapshot.ImageAlternates.ToList() : null
        };
        entry.PostHash = GamepackContentComparer.HashImage(write.Image!.ContentType, write.ImageBytes!);
        break;
    }

    return entry;
  }

  // After a failed apply: delete the backup when the undo worked. Keep it when the undo failed, so the client can still read it.
  private void DropTakeOverBackup(string? backupId, bool undone) {
    if (backupId is null || _takeOverBackups is null) {
      return;
    }

    if (!undone) {
      LogTakeOverBackupKept(_logger, backupId);
      return;
    }

    try {
      _takeOverBackups.Delete(backupId);
      LogTakeOverBackupDeleted(_logger, backupId);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      LogTakeOverBackupKept(_logger, backupId);
    }
  }

  // ---- restore of a backup ----

  /// <summary>
  /// Puts back the old content of the objects in a take-over backup (feature 149). The restore is atomic: a refusal or a failed write
  /// leaves the state as before. The backup file stays. The caller holds the install gate.
  /// </summary>
  /// <param name="backupId">The backup id. The store checks its form before any file access.</param>
  /// <param name="mode">Strict for the public route. Force skips the owner check only.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<TakeOverRestoreOutcome> RestoreTakeOverAsync(string backupId, TakeOverRestoreMode mode, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(backupId);
    var backup = _takeOverBackups is not null && _takeOverBackups.IsValidId(backupId) ? _takeOverBackups.Read(backupId) : null;
    if (backup is null) {
      return new TakeOverRestoreOutcome { Status = TakeOverRestoreStatus.NotFound, BackupId = backupId };
    }

    var undo = new List<Snapshot>();
    var reasons = new List<TakeOverRestoreReason>();
    GamepackRegistry registryBefore;
    bool registryExisted;
    try {
      registryExisted = _store.RegistryExists;
      registryBefore = _store.Load();
      await CheckBackupAsync(backup, mode, registryBefore, undo, reasons, ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      return new TakeOverRestoreOutcome { Status = TakeOverRestoreStatus.Failed, BackupId = backupId, ErrorMessage = ex.Message };
    }

    if (reasons.Count > 0) {
      return new TakeOverRestoreOutcome { Status = TakeOverRestoreStatus.Refused, BackupId = backupId, Reasons = reasons };
    }

    var touched = new List<Snapshot>();
    var registryTouched = false;
    try {
      foreach (var entry in backup.Entries) {
        ct.ThrowIfCancellationRequested();
        var snapshot = undo.First(s => s.Matches(entry.Type, entry.ObjectId));
        touched.Add(snapshot);
        _gate.BeforeWrite(GamepackWriteOperations.TakeOverRestore, GamepackRegistry.ObjectKey(entry.Type, entry.Name));
        await RestoreObjectAsync(ToOldSnapshot(entry)).ConfigureAwait(false);
      }

      registryTouched = true;
      _gate.BeforeWrite(GamepackWriteOperations.TakeOverRestore, backup.Gamepack);
      _store.Update(registry => RemoveOwnership(registry, backup));
    }
    catch (Exception ex) {
      var failed = await RestoreAsync(registryTouched, registryBefore, registryExisted, previousTouched: false, backup.Gamepack, null, touched).ConfigureAwait(false);
      return new TakeOverRestoreOutcome {
        Status = failed.Count == 0 ? TakeOverRestoreStatus.Failed : TakeOverRestoreStatus.PartialRestore,
        BackupId = backupId,
        ErrorMessage = ex.Message
      };
    }

    LogTakeOverRestored(_logger, backup.Gamepack, backup.Entries.Count, backupId);
    return new TakeOverRestoreOutcome { Status = TakeOverRestoreStatus.Restored, BackupId = backupId, Restored = backup.Entries.Count };
  }

  private async Task CheckBackupAsync(
      TakeOverBackup backup, TakeOverRestoreMode mode, GamepackRegistry registry, List<Snapshot> undo, List<TakeOverRestoreReason> reasons, CancellationToken ct) {
    var owners = new GamepackOwnerResolver(registry);
    var installed = registry.Gamepacks.ContainsKey(GamepackNameRules.Key(backup.Gamepack));
    foreach (var entry in backup.Entries) {
      var snapshot = await TakeSnapshotAsync(entry.Type, GamepackRegistry.ObjectKey(entry.Type, entry.Name), entry.ObjectId, ct).ConfigureAwait(false);
      if (!snapshot.Existed) {
        reasons.Add(new TakeOverRestoreReason(
          TakeOverRestoreReasonCodes.ObjectMissing, entry.Type, entry.Name, $"The {entry.Type} '{entry.Name}' does not exist any more."));
        continue;
      }

      undo.Add(snapshot);
      var owner = owners.Find(entry.Type, entry.ObjectId);
      var ownedByPack = installed && owner is not null && GamepackNameRules.Comparer.Equals(owner.Gamepack, backup.Gamepack);
      if (mode == TakeOverRestoreMode.Strict && !ownedByPack) {
        // The owner check comes first. A second restore of one backup gives only this reason.
        reasons.Add(new TakeOverRestoreReason(
          TakeOverRestoreReasonCodes.OwnerChanged, entry.Type, entry.Name, $"The gamepack '{backup.Gamepack}' does not own the {entry.Type} '{entry.Name}' any more."));
        continue;
      }

      if (!string.Equals(LiveHash(snapshot), entry.PostHash, StringComparison.Ordinal)) {
        reasons.Add(new TakeOverRestoreReason(
          TakeOverRestoreReasonCodes.ObjectChanged, entry.Type, entry.Name, $"The {entry.Type} '{entry.Name}' changed after the install."));
      }
    }
  }

  private static string LiveHash(Snapshot snapshot) => snapshot.Type switch {
    GamepackObjectTypes.Sequence => GamepackContentComparer.Hash(snapshot.Sequence!),
    GamepackObjectTypes.Command => GamepackContentComparer.Hash(snapshot.Command!),
    _ => GamepackContentComparer.HashImage(snapshot.ImageContentType, snapshot.ImageBytes!)
  };

  private static Snapshot ToOldSnapshot(TakeOverBackupEntry entry) {
    var snapshot = new Snapshot { Type = entry.Type, Key = GamepackRegistry.ObjectKey(entry.Type, entry.Name), Id = entry.ObjectId, Existed = true };
    switch (entry.Type) {
      case GamepackObjectTypes.Sequence:
        snapshot.Sequence = entry.Sequence!.Value.Deserialize<CommandSequence>(GamepackFileSerializer.SequenceJsonOptions)
          ?? throw new InvalidOperationException("The backup holds no sequence.");
        break;
      case GamepackObjectTypes.Command:
        snapshot.Command = entry.Command!.Value.Deserialize<Command>(GamepackFileSerializer.CommandJsonOptions)
          ?? throw new InvalidOperationException("The backup holds no command.");
        break;
      default:
        snapshot.ImageBytes = Convert.FromBase64String(entry.Image!.DataBase64);
        snapshot.ImageContentType = entry.Image.ContentType;
        snapshot.ImageFilename = entry.Image.Filename;
        snapshot.ImageAlternates = entry.Image.Alternates?.ToList();
        break;
    }

    return snapshot;
  }

  // The restored objects belong to the user again: remove their owner entries and their refs in the gamepack record.
  private static void RemoveOwnership(GamepackRegistry registry, TakeOverBackup backup) {
    foreach (var entry in backup.Entries) {
      var keys = registry.Owners
        .Where(p => string.Equals(GamepackRegistry.TypeOfKey(p.Key), entry.Type, StringComparison.OrdinalIgnoreCase)
          && string.Equals(p.Value.Id, entry.ObjectId, StringComparison.OrdinalIgnoreCase)
          && GamepackNameRules.Comparer.Equals(p.Value.Gamepack, backup.Gamepack))
        .Select(p => p.Key)
        .ToList();
      foreach (var key in keys) {
        registry.Owners.Remove(key);
      }

      if (registry.Gamepacks.TryGetValue(GamepackNameRules.Key(backup.Gamepack), out var record)) {
        record.Objects.RemoveAll(o => string.Equals(o.Type, entry.Type, StringComparison.OrdinalIgnoreCase)
          && string.Equals(o.Id, entry.ObjectId, StringComparison.OrdinalIgnoreCase));
      }
    }
  }

  [LoggerMessage(EventId = 1432, Level = LogLevel.Information, Message = "The gamepack {Gamepack} took over {Count} objects of the user. Take-over backup: {BackupId}.")]
  private static partial void LogTakeOver(ILogger logger, string gamepack, int count, string backupId);

  [LoggerMessage(EventId = 1433, Level = LogLevel.Information, Message = "The take-over backup {BackupId} was deleted, because the install did not work.")]
  private static partial void LogTakeOverBackupDeleted(ILogger logger, string backupId);

  [LoggerMessage(EventId = 1434, Level = LogLevel.Warning, Message = "The take-over backup {BackupId} stays on disk. The undo of the failed install was not complete.")]
  private static partial void LogTakeOverBackupKept(ILogger logger, string backupId);

  [LoggerMessage(EventId = 1435, Level = LogLevel.Information, Message = "The gamepack {Gamepack} gave back {Count} objects to the user. Take-over backup: {BackupId}.")]
  private static partial void LogTakeOverRestored(ILogger logger, string gamepack, int count, string backupId);
}
