using System.IO.Compression;
using System.Text.Json;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using GameBot.Service.Contracts.Backup;

namespace GameBot.Service.Services;

/// <summary>Assembles and restores backup archives for commands, sequences, and their images.</summary>
internal sealed class BackupService {
  private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = true };
  private const string ManifestVersion = "1.0";

  private readonly ICommandRepository _commands;
  private readonly ISequenceRepository _sequences;
  private readonly IImageRepository _images;

  public BackupService(ICommandRepository commands, ISequenceRepository sequences, IImageRepository images) {
    _commands = commands;
    _sequences = sequences;
    _images = images;
  }

  /// <summary>
  /// Assembles a zip archive of the selected commands, sequences, and their referenced images.
  /// </summary>
  /// <param name="request">IDs of commands and sequences to include.</param>
  /// <param name="outputStream">Writable stream that receives the archive bytes.</param>
  /// <param name="ct">Cancellation token.</param>
  public async Task CreateBackupAsync(BackupRequestDto request, Stream outputStream, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(request);
    var commandSet = new Dictionary<string, Command>(StringComparer.Ordinal);
    var sequenceSet = new Dictionary<string, CommandSequence>(StringComparer.Ordinal);
    var imageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    foreach (var id in request.CommandIds) {
      var cmd = await _commands.GetAsync(id, ct).ConfigureAwait(false);
      if (cmd is not null) commandSet[cmd.Id] = cmd;
    }

    foreach (var seqId in request.SequenceIds) {
      var seq = await _sequences.GetAsync(seqId).ConfigureAwait(false);
      if (seq is null) continue;
      sequenceSet[seq.Id] = seq;
      foreach (var cmdId in CollectCommandIds(seq.Steps)) {
        if (!commandSet.ContainsKey(cmdId)) {
          var cmd = await _commands.GetAsync(cmdId, ct).ConfigureAwait(false);
          if (cmd is not null) commandSet[cmd.Id] = cmd;
        }
      }
    }

    foreach (var cmd in commandSet.Values)
      foreach (var imgId in ImageReferenceExtractor.ExtractImageIds(cmd))
        imageIds.Add(imgId);

    foreach (var seq in sequenceSet.Values)
      foreach (var imgId in ImageReferenceExtractor.ExtractImageIds(seq.Steps))
        imageIds.Add(imgId);

    using var ms = new MemoryStream();
    using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
      var manifest = new {
        version = ManifestVersion,
        createdAt = DateTimeOffset.UtcNow,
        commandCount = commandSet.Count,
        sequenceCount = sequenceSet.Count,
        imageCount = imageIds.Count
      };
      await WriteJsonEntryAsync(archive, "manifest.json", manifest, ct).ConfigureAwait(false);

      foreach (var cmd in commandSet.Values)
        await WriteJsonEntryAsync(archive, $"commands/{cmd.Id}.json", cmd, ct).ConfigureAwait(false);

      foreach (var seq in sequenceSet.Values)
        await WriteJsonEntryAsync(archive, $"sequences/{seq.Id}.json", seq, ct).ConfigureAwait(false);

      foreach (var imgId in imageIds) {
        var asset = await _images.GetAsync(imgId, ct).ConfigureAwait(false);
        if (asset is null) continue;
        var ext = asset.ContentType.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpeg" : ".png";
        var entry = archive.CreateEntry($"images/{imgId}{ext}", CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        var imgStream = await _images.OpenReadAsync(imgId, ct).ConfigureAwait(false);
        if (imgStream is not null) {
          using (imgStream) await imgStream.CopyToAsync(entryStream, ct).ConfigureAwait(false);
        }
      }
    } // ZipArchive.Dispose writes end-of-central-directory to ms (sync is safe on MemoryStream)
    ms.Position = 0;
    await ms.CopyToAsync(outputStream, ct).ConfigureAwait(false);
  }

  /// <summary>
  /// Reads the archive, validates its format, and returns a conflict report without modifying data.
  /// </summary>
  /// <param name="archiveStream">Stream containing the zip archive.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A <see cref="ConflictReportDto"/> describing name/ID collisions with existing objects.</returns>
  /// <exception cref="BackupFormatException">The archive is invalid, missing a manifest, or references images not present in the archive.</exception>
  public async Task<ConflictReportDto> DryRunRestoreAsync(Stream archiveStream, CancellationToken ct = default) {
    ZipArchive archive;
    try { archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true); }
    catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException) {
      throw new BackupFormatException("Archive is not a valid zip file.", ex);
    }
    using (archive) {

    ValidateManifest(archive);

    var archiveCommandNames = ReadJsonNames<Command>(archive, "commands/", cmd => cmd?.Name);
    var archiveSequenceNames = ReadJsonNames<CommandSequence>(archive, "sequences/", seq => seq?.Name);
    var archiveImageIds = ReadImageIds(archive);

    ValidateMissingImages(archive, archiveImageIds);

    var existingCommands = await _commands.ListAsync(ct).ConfigureAwait(false);
    var existingSequences = await _sequences.ListAsync().ConfigureAwait(false);
    var existingImageIds = await _images.ListIdsAsync(ct).ConfigureAwait(false);

    var conflictingCommands = archiveCommandNames
      .Where(n => existingCommands.Any(c => string.Equals(c.Name, n, StringComparison.Ordinal)))
      .ToList();
    var conflictingSequences = archiveSequenceNames
      .Where(n => existingSequences.Any(s => string.Equals(s.Name, n, StringComparison.Ordinal)))
      .ToList();
    var conflictingImages = archiveImageIds
      .Where(id => existingImageIds.Contains(id, StringComparer.OrdinalIgnoreCase))
      .ToList();

    return new ConflictReportDto {
      HasConflicts = conflictingCommands.Count > 0 || conflictingSequences.Count > 0 || conflictingImages.Count > 0,
      ConflictingCommandNames = conflictingCommands,
      ConflictingSequenceNames = conflictingSequences,
      ConflictingImageIds = conflictingImages,
      TotalCommands = archiveCommandNames.Count,
      TotalSequences = archiveSequenceNames.Count,
      TotalImages = archiveImageIds.Count
    };
    } // end using(archive)
  }

  /// <summary>
  /// Applies the archive: overwrites conflicting objects and creates new ones. Rolls back on failure.
  /// </summary>
  /// <param name="archiveStream">Stream containing the zip archive.</param>
  /// <param name="ct">Cancellation token.</param>
  /// <returns>A <see cref="RestoreResultDto"/> with counts and rollback status.</returns>
  /// <exception cref="BackupFormatException">The archive is invalid or references images not present in the archive.</exception>
  public async Task<RestoreResultDto> ApplyRestoreAsync(Stream archiveStream, CancellationToken ct = default) {
    ZipArchive archive;
    try { archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true); }
    catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException) {
      throw new BackupFormatException("Archive is not a valid zip file.", ex);
    }
    using (archive) {

    ValidateManifest(archive);

    var commands = ParseJsonEntries<Command>(archive, "commands/");
    var sequences = ParseJsonEntries<CommandSequence>(archive, "sequences/");
    var imageEntries = ReadImageEntries(archive);

    ValidateMissingImages(imageEntries.Keys, commands, sequences);

    var existingCommands = await _commands.ListAsync(ct).ConfigureAwait(false);
    var existingSequences = await _sequences.ListAsync().ConfigureAwait(false);
    var existingImageIds = await _images.ListIdsAsync(ct).ConfigureAwait(false);

    var conflictingCmdIds = existingCommands
      .Where(c => commands.Any(a => string.Equals(a.Name, c.Name, StringComparison.Ordinal)))
      .Select(c => c.Id).ToList();
    var conflictingSeqIds = existingSequences
      .Where(s => sequences.Any(a => string.Equals(a.Name, s.Name, StringComparison.Ordinal)))
      .Select(s => s.Id).ToList();
    var conflictingImageIds = imageEntries.Keys
      .Where(id => existingImageIds.Contains(id, StringComparer.OrdinalIgnoreCase))
      .ToList();

    var originalCommands = existingCommands
      .Where(c => conflictingCmdIds.Contains(c.Id)).ToList();
    var originalSequences = existingSequences
      .Where(s => conflictingSeqIds.Contains(s.Id)).ToList();

    var originalImageData = new Dictionary<string, (byte[] Data, string ContentType, string? Filename)>(StringComparer.OrdinalIgnoreCase);
    foreach (var imgId in conflictingImageIds) {
      var asset = await _images.GetAsync(imgId, ct).ConfigureAwait(false);
      var stream = await _images.OpenReadAsync(imgId, ct).ConfigureAwait(false);
      if (stream is not null && asset is not null) {
        using (stream) {
          var ms = new MemoryStream();
          await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
          originalImageData[imgId] = (ms.ToArray(), asset.ContentType, asset.Filename);
        }
      }
    }

    var stagingDir = Path.Combine(Path.GetTempPath(), "gamebot-restore-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(stagingDir);

    try {
      foreach (var cmd in commands) {
        var path = Path.Combine(stagingDir, $"cmd_{cmd.Id}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(cmd, JsonOpts), ct).ConfigureAwait(false);
      }
      foreach (var seq in sequences) {
        var path = Path.Combine(stagingDir, $"seq_{seq.Id}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(seq, JsonOpts), ct).ConfigureAwait(false);
      }
    }
    catch (Exception stagingEx) {
      Directory.Delete(stagingDir, recursive: true);
      return new RestoreResultDto { RolledBack = true, ErrorMessage = $"Staging failed: {stagingEx.Message}" };
    }

    foreach (var imgId in conflictingImageIds)
      await _images.DeleteAsync(imgId, ct).ConfigureAwait(false);
    foreach (var cmdId in conflictingCmdIds)
      await _commands.DeleteAsync(cmdId, ct).ConfigureAwait(false);
    foreach (var seqId in conflictingSeqIds)
      await _sequences.DeleteAsync(seqId).ConfigureAwait(false);

    try {
      foreach (var cmd in commands) {
        var path = Path.Combine(stagingDir, $"cmd_{cmd.Id}.json");
        var loaded = JsonSerializer.Deserialize<Command>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), JsonOpts)!;
        await _commands.AddAsync(loaded, ct).ConfigureAwait(false);
      }
      foreach (var seq in sequences) {
        var path = Path.Combine(stagingDir, $"seq_{seq.Id}.json");
        var loaded = JsonSerializer.Deserialize<CommandSequence>(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false), JsonOpts)!;
        await _sequences.CreateAsync(loaded).ConfigureAwait(false);
      }
      foreach (var (imgId, (data, contentType, filename)) in imageEntries) {
        using var ms = new MemoryStream(data);
        await _images.SaveAsync(imgId, ms, contentType, filename, overwrite: true, ct).ConfigureAwait(false);
      }
    }
    catch (Exception applyEx) {
      var rollbackError = await TryRollbackAsync(originalCommands, originalSequences, originalImageData, ct).ConfigureAwait(false);
      Directory.Delete(stagingDir, recursive: true);
      var msg = rollbackError is not null
        ? $"Apply failed: {applyEx.Message}. Rollback also encountered an error: {rollbackError}"
        : $"Apply failed: {applyEx.Message}. Data restored.";
      return new RestoreResultDto { RolledBack = true, ErrorMessage = msg };
    }

    Directory.Delete(stagingDir, recursive: true);
    return new RestoreResultDto {
      RestoredCommands = commands.Count,
      RestoredSequences = sequences.Count,
      RestoredImages = imageEntries.Count
    };
    } // end using(archive)
  }

  private static IEnumerable<string> CollectCommandIds(IEnumerable<SequenceStep> steps) {
    foreach (var step in steps) {
      if (!string.IsNullOrWhiteSpace(step.CommandId)) yield return step.CommandId;
      if (step.StepType == SequenceStepType.Loop && step.Body.Count > 0)
        foreach (var id in CollectCommandIds(step.Body)) yield return id;
    }
  }

  private static void ValidateManifest(ZipArchive archive) {
    var entry = archive.GetEntry("manifest.json")
      ?? throw new BackupFormatException("Archive is missing manifest.json.");
    using var stream = entry.Open();
    using var doc = JsonDocument.Parse(stream);
    if (!doc.RootElement.TryGetProperty("version", out var v) || v.GetString() != ManifestVersion)
      throw new BackupFormatException($"Unsupported archive version. Expected '{ManifestVersion}'.");
  }

  private static List<string> ReadJsonNames<T>(ZipArchive archive, string prefix, Func<T?, string?> getName) {
    var names = new List<string>();
    foreach (var entry in archive.Entries) {
      if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
      if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
      using var stream = entry.Open();
      var obj = JsonSerializer.Deserialize<T>(stream, JsonOpts);
      var name = getName(obj);
      if (!string.IsNullOrWhiteSpace(name)) names.Add(name!);
    }
    return names;
  }

  private static List<string> ReadImageIds(ZipArchive archive) {
    return archive.Entries
      .Where(e => e.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
      .Select(e => Path.GetFileNameWithoutExtension(e.Name))
      .Where(id => !string.IsNullOrWhiteSpace(id))
      .ToList()!;
  }

  private static void ValidateMissingImages(ZipArchive archive, IReadOnlyList<string> archiveImageIds) {
    var missingIds = new List<string>();
    foreach (var entry in archive.Entries) {
      if (!entry.FullName.StartsWith("commands/", StringComparison.OrdinalIgnoreCase)
          && !entry.FullName.StartsWith("sequences/", StringComparison.OrdinalIgnoreCase)) continue;
      if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
      using var stream = entry.Open();
      using var doc = JsonDocument.Parse(stream);
      CollectImageRefsFromJson(doc.RootElement, archiveImageIds, missingIds);
    }
    if (missingIds.Count > 0)
      throw new BackupFormatException($"Archive is missing image entries for: {string.Join(", ", missingIds)}");
  }

  private static void ValidateMissingImages(
    IEnumerable<string> archiveImageIds,
    IReadOnlyList<Command> commands,
    IReadOnlyList<CommandSequence> sequences) {
    var idSet = new HashSet<string>(archiveImageIds, StringComparer.OrdinalIgnoreCase);
    var missing = new List<string>();

    foreach (var cmd in commands)
      foreach (var imgId in ImageReferenceExtractor.ExtractImageIds(cmd))
        if (!idSet.Contains(imgId) && !missing.Contains(imgId)) missing.Add(imgId);

    foreach (var seq in sequences)
      foreach (var imgId in ImageReferenceExtractor.ExtractImageIds(seq.Steps))
        if (!idSet.Contains(imgId) && !missing.Contains(imgId)) missing.Add(imgId);

    if (missing.Count > 0)
      throw new BackupFormatException($"Archive is missing image entries for: {string.Join(", ", missing)}");
  }

  private static void CollectImageRefsFromJson(JsonElement element, IReadOnlyList<string> archiveImageIds, List<string> missing) {
    if (element.ValueKind == JsonValueKind.Object) {
      foreach (var prop in element.EnumerateObject()) {
        if ((prop.Name.Equals("referenceImageId", StringComparison.OrdinalIgnoreCase)
             || prop.Name.Equals("targetId", StringComparison.OrdinalIgnoreCase))
            && prop.Value.ValueKind == JsonValueKind.String) {
          var val = prop.Value.GetString();
          if (!string.IsNullOrWhiteSpace(val)
              && !archiveImageIds.Any(id => string.Equals(id, val, StringComparison.OrdinalIgnoreCase))
              && !missing.Contains(val))
            missing.Add(val);
        }
        CollectImageRefsFromJson(prop.Value, archiveImageIds, missing);
      }
    }
    else if (element.ValueKind == JsonValueKind.Array) {
      foreach (var item in element.EnumerateArray())
        CollectImageRefsFromJson(item, archiveImageIds, missing);
    }
  }

  private static List<T> ParseJsonEntries<T>(ZipArchive archive, string prefix) {
    var list = new List<T>();
    foreach (var entry in archive.Entries) {
      if (!entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
      if (!entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
      using var stream = entry.Open();
      var obj = JsonSerializer.Deserialize<T>(stream, JsonOpts);
      if (obj is not null) list.Add(obj);
    }
    return list;
  }

  private static Dictionary<string, (byte[] Data, string ContentType, string? Filename)> ReadImageEntries(ZipArchive archive) {
    var result = new Dictionary<string, (byte[], string, string?)>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in archive.Entries) {
      if (!entry.FullName.StartsWith("images/", StringComparison.OrdinalIgnoreCase)) continue;
      var id = Path.GetFileNameWithoutExtension(entry.Name);
      if (string.IsNullOrWhiteSpace(id)) continue;
      var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
      var contentType = ext == ".jpeg" || ext == ".jpg" ? "image/jpeg" : "image/png";
      using var stream = entry.Open();
      var ms = new MemoryStream();
      stream.CopyTo(ms);
      result[id] = (ms.ToArray(), contentType, entry.Name);
    }
    return result;
  }

  private async Task<string?> TryRollbackAsync(
    IReadOnlyList<Command> originalCommands,
    IReadOnlyList<CommandSequence> originalSequences,
    IReadOnlyDictionary<string, (byte[] Data, string ContentType, string? Filename)> originalImages,
    CancellationToken ct) {
    try {
      foreach (var cmd in originalCommands)
        await _commands.AddAsync(cmd, ct).ConfigureAwait(false);
      foreach (var seq in originalSequences)
        await _sequences.CreateAsync(seq).ConfigureAwait(false);
      foreach (var (imgId, (data, contentType, filename)) in originalImages) {
        using var ms = new MemoryStream(data);
        await _images.SaveAsync(imgId, ms, contentType, filename, overwrite: true, ct).ConfigureAwait(false);
      }
      return null;
    }
    catch (Exception ex) {
      return ex.Message;
    }
  }

  private static async Task WriteJsonEntryAsync<T>(ZipArchive archive, string entryName, T value, CancellationToken ct) {
    var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
    using var stream = entry.Open();
    await JsonSerializer.SerializeAsync(stream, value, JsonOpts, ct).ConfigureAwait(false);
  }
}
