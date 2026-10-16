namespace GameBot.Domain.Gamepacks;

/// <summary>
/// The installed state of one gamepack, recorded before a managed call (feature 148). The restore of a failed managed call
/// uses it to put the state back.
/// </summary>
public sealed class GamepackInstalledSnapshot {
  /// <summary>A copy of the registry record.</summary>
  public required InstalledGamepack Record { get; init; }

  /// <summary>The text of the previous-version file, or null when none existed.</summary>
  public string? PreviousFileText { get; init; }

  /// <summary>
  /// The live content of the installed version as a gamepack file text, or null when the caller did not ask for it. A managed
  /// rollback asks for it, because its restore cannot call the rollback a second time.
  /// </summary>
  public string? LiveFileText { get; init; }
}

public sealed partial class GamepackService {
  /// <summary>Records the installed state of a gamepack. It takes no lock. It changes nothing.</summary>
  /// <param name="name">The gamepack name. Case is ignored.</param>
  /// <param name="includeLiveContent">True to also read the live content of the installed version.</param>
  /// <param name="ct">The cancellation token.</param>
  /// <returns>The snapshot, or null when no gamepack has this name.</returns>
  public async Task<GamepackInstalledSnapshot?> GetInstalledSnapshotAsync(string name, bool includeLiveContent, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(name);
    if (!GamepackNameRules.IsValid(name)) {
      return null;
    }

    var registry = _store.Load();
    if (!registry.Gamepacks.TryGetValue(GamepackNameRules.Key(name), out var record)) {
      return null;
    }

    var live = includeLiveContent
      ? await _applier.CaptureLiveTextAsync(record, registry, ct).ConfigureAwait(false)
      : null;
    return new GamepackInstalledSnapshot { Record = record, PreviousFileText = _store.ReadPrevious(record.Name), LiveFileText = live };
  }

  /// <summary>
  /// Puts back the kept previous version of a gamepack (the registry field and the previous-version file) from a snapshot.
  /// It takes the gate. The content of the objects does not change.
  /// </summary>
  /// <param name="snapshot">The snapshot from <see cref="GetInstalledSnapshotAsync"/>.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task RestorePreviousMetadataAsync(GamepackInstalledSnapshot snapshot, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(snapshot);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      WritePreviousMetadata(snapshot);
    }
    finally {
      _gate.Release();
    }
  }

  /// <summary>
  /// Installs the live content that a snapshot holds, and puts back the kept previous version. A managed rollback uses it
  /// to restore the state, because the restore must not call the rollback a second time. It does not check the signature.
  /// </summary>
  /// <param name="snapshot">A snapshot with live content.</param>
  /// <param name="ct">The cancellation token.</param>
  /// <returns>Null when the state is back. Else the error text.</returns>
  public async Task<string?> ReapplyInstalledAsync(GamepackInstalledSnapshot snapshot, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(snapshot);
    if (snapshot.LiveFileText is null) {
      return "The snapshot has no live content.";
    }

    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var parsed = GamepackFileSerializer.Parse(snapshot.LiveFileText);
      if (parsed.File is null) {
        return "The recorded content is not valid.";
      }

      var check = await _validator.ValidateAsync(parsed.File, parsed.Problems, ct, checkApiVersion: false).ConfigureAwait(false);
      if (!check.IsValid || check.Plan is null) {
        return "The recorded content does not pass the check.";
      }

      var applied = await _applier.ApplyAsync(check.Plan, GamepackPreviousHandling.Keep, ct).ConfigureAwait(false);
      if (applied.Status != GamepackApplyStatus.Applied) {
        return applied.Error?.Message ?? "The apply failed.";
      }

      WritePreviousMetadata(snapshot);
      return null;
    }
    finally {
      _gate.Release();
    }
  }

  /// <summary>
  /// Removes an installed gamepack and its objects. Only the restore of a failed first install uses it. No route calls it
  /// (there is no uninstall, feature 143).
  /// </summary>
  /// <param name="name">The gamepack name.</param>
  /// <param name="fileText">
  /// The text of the file that the failed install used, or null. A failed install can leave objects that the registry does not
  /// list. The check of the install rejected any object of the user with the same name, so the removal deletes the objects of
  /// this file by name.
  /// </param>
  /// <param name="ct">The cancellation token.</param>
  /// <returns>The keys of the objects that the removal could not delete.</returns>
  public async Task<IReadOnlyList<string>> RemoveInstalledAsync(string name, string? fileText, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(name);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var failed = new List<string>();
      var registry = _store.Load();
      var owned = registry.Owners
        .Where(p => GamepackNameRules.Comparer.Equals(p.Value.Gamepack, name))
        .Select(p => (Type: GamepackRegistry.TypeOfKey(p.Key), p.Value.Id, Key: p.Key))
        .ToList();
      if (registry.Gamepacks.TryGetValue(GamepackNameRules.Key(name), out var record)) {
        foreach (var o in record.Objects) {
          if (!owned.Any(x => x.Type == o.Type && string.Equals(x.Id, o.Id, StringComparison.OrdinalIgnoreCase))) {
            owned.Add((o.Type, o.Id, GamepackRegistry.ObjectKey(o.Type, o.Name)));
          }
        }
      }

      foreach (var (type, id, key) in owned) {
        try {
          switch (type) {
            case GamepackObjectTypes.Sequence:
              await _sequences.DeleteAsync(id).ConfigureAwait(false);
              break;
            case GamepackObjectTypes.Command:
              await _commands.DeleteAsync(id, ct).ConfigureAwait(false);
              break;
            case GamepackObjectTypes.Image:
              await _images.DeleteAsync(id, ct).ConfigureAwait(false);
              break;
          }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
          failed.Add(key);
        }
      }

      var hint = fileText is null ? null : GamepackFileSerializer.Parse(fileText).File;
      if (hint is not null) {
        await RemoveByHintAsync(hint, failed, ct).ConfigureAwait(false);
      }

      try {
        _store.Update(r => {
          foreach (var key in r.Owners.Where(p => GamepackNameRules.Comparer.Equals(p.Value.Gamepack, name)).Select(p => p.Key).ToList()) {
            r.Owners.Remove(key);
          }

          r.Gamepacks.Remove(GamepackNameRules.Key(name));
        });
        _store.DeletePrevious(name);
      }
      catch (Exception ex) when (ex is not OperationCanceledException) {
        failed.Add("registry");
      }

      return failed;
    }
    finally {
      _gate.Release();
    }
  }

  private async Task RemoveByHintAsync(GamepackFile hint, List<string> failed, CancellationToken ct) {
    try {
      var sequenceNames = hint.Sequences.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var sequence in (await _sequences.ListAsync().ConfigureAwait(false)).Where(s => sequenceNames.Contains(s.Name))) {
        await _sequences.DeleteAsync(sequence.Id).ConfigureAwait(false);
      }

      var commandNames = hint.Commands.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var command in (await _commands.ListAsync(ct).ConfigureAwait(false)).Where(c => commandNames.Contains(c.Name))) {
        await _commands.DeleteAsync(command.Id, ct).ConfigureAwait(false);
      }

      var existingImages = (await _images.ListIdsAsync(ct).ConfigureAwait(false)).ToHashSet(StringComparer.OrdinalIgnoreCase);
      foreach (var image in hint.Images.Where(i => i.Id is not null && existingImages.Contains(i.Id))) {
        await _images.DeleteAsync(image.Id!, ct).ConfigureAwait(false);
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      failed.Add("files:" + ex.Message);
    }
  }

  private void WritePreviousMetadata(GamepackInstalledSnapshot snapshot) {
    var name = snapshot.Record.Name;
    _store.Update(r => {
      if (r.Gamepacks.TryGetValue(GamepackNameRules.Key(name), out var current)) {
        current.Previous = snapshot.Record.Previous;
      }
    });
    if (snapshot.PreviousFileText is null) {
      _store.DeletePrevious(name);
    }
    else {
      _store.WritePrevious(name, snapshot.PreviousFileText);
    }
  }
}
