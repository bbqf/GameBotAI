using System.Globalization;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using GameBot.Domain.Triggers.Evaluators;

namespace GameBot.Domain.Gamepacks;

/// <summary>
/// The one check for a dry run, an install, and a rollback (feature 143, FR-013, FR-014, FR-018). It checks the
/// structure, the references, and the conflicts. It collects all problems. When there is no problem it also builds
/// the <see cref="GamepackPlan"/> and rewrites the references in the file objects (FR-020).
/// </summary>
public sealed class GamepackValidator {
  /// <summary>The largest decoded image size in bytes (FR-023).</summary>
  public const int MaxImageBytes = 10_000_000;

  // 10,000,000 bytes need about 13,333,336 base64 characters. This bound stops a decode of a huge text.
  private const int MaxImageBase64Chars = 13_400_000;

  private readonly ISequenceRepository _sequences;
  private readonly ICommandRepository _commands;
  private readonly IImageRepository _images;
  private readonly IGamepackRegistryStore _registryStore;

  /// <summary>Creates the validator.</summary>
  /// <param name="sequences">The sequence store.</param>
  /// <param name="commands">The command store.</param>
  /// <param name="images">The image store.</param>
  /// <param name="registryStore">The registry store.</param>
  public GamepackValidator(ISequenceRepository sequences, ICommandRepository commands, IImageRepository images, IGamepackRegistryStore registryStore) {
    _sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
    _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    _images = images ?? throw new ArgumentNullException(nameof(images));
    _registryStore = registryStore ?? throw new ArgumentNullException(nameof(registryStore));
  }

  /// <summary>Checks a file against the current installation.</summary>
  /// <param name="file">The file. Its objects change when there is no problem (references are rewritten).</param>
  /// <param name="parseProblems">Problems that the parse found, or null.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<GamepackValidationResult> ValidateAsync(GamepackFile file, IReadOnlyList<GamepackProblem>? parseProblems = null, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(file);
    var problems = new List<GamepackProblem>();
    if (parseProblems is not null) {
      problems.AddRange(parseProblems);
    }

    var decoded = CheckStructure(file, problems);

    var existingSequences = await _sequences.ListAsync().ConfigureAwait(false);
    var existingCommands = await _commands.ListAsync(ct).ConfigureAwait(false);
    var existingImageIds = await _images.ListIdsAsync(ct).ConfigureAwait(false);
    var registry = _registryStore.Load();
    var owners = new GamepackOwnerResolver(registry);
    var packName = file.Manifest?.Name;

    var sequenceDecisions = DecideSequences(file, existingSequences, owners, packName, problems);
    var commandDecisions = DecideCommands(file, existingCommands, owners, packName, problems);
    var imageDecisions = DecideImages(file, existingImageIds, owners, packName, problems);

    var removals = FindRemovals(registry, owners, packName, existingSequences, existingCommands, existingImageIds,
      sequenceDecisions, commandDecisions, imageDecisions);

    CheckReferences(file, existingCommands, existingImageIds, removals, problems);

    if (problems.Count > 0 || file.Manifest is null) {
      return new GamepackValidationResult { Problems = problems };
    }

    var plan = BuildPlan(file, existingSequences, existingCommands, sequenceDecisions, commandDecisions, imageDecisions, decoded, removals);
    return new GamepackValidationResult { Problems = problems, Plan = plan };
  }

  // ---- structure ----

  private static Dictionary<string, byte[]> CheckStructure(GamepackFile file, List<GamepackProblem> problems) {
    var decoded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    var manifest = file.Manifest;
    if (manifest is not null) {
      var nameError = GamepackNameRules.Validate(manifest.Name);
      if (nameError is not null) {
        problems.Add(Structure(GamepackObjectTypes.Manifest, manifest.Name, nameError));
      }

      if (string.IsNullOrWhiteSpace(manifest.Version)) {
        problems.Add(Structure(GamepackObjectTypes.Manifest, manifest.Name, "The gamepack version is required."));
      }
    }

    CheckNames(file.Sequences.Select(s => s.Name), GamepackObjectTypes.Sequence, problems);
    CheckNames(file.Commands.Select(c => c.Name), GamepackObjectTypes.Command, problems);
    CheckImages(file.Images, decoded, problems);
    return decoded;
  }

  private static void CheckNames(IEnumerable<string?> names, string type, List<GamepackProblem> problems) {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var name in names) {
      if (string.IsNullOrWhiteSpace(name)) {
        problems.Add(Structure(type, null, $"A {type} in the file has no name."));
      }
      else if (!seen.Add(name)) {
        problems.Add(Structure(type, name, $"The file has two {type}s with the name '{name}'. Names are not case-sensitive."));
      }
    }
  }

  private static void CheckImages(IReadOnlyList<GamepackImage> images, Dictionary<string, byte[]> decoded, List<GamepackProblem> problems) {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var image in images) {
      var id = image.Id;
      if (!ReferenceImageIdValidator.IsValid(id)) {
        problems.Add(Structure(GamepackObjectTypes.Image, id, "The image id is not valid. Use 1 to 128 letters, digits, underscore, or hyphen."));
        continue;
      }

      if (!seen.Add(id!)) {
        problems.Add(Structure(GamepackObjectTypes.Image, id, $"The file has two images with the id '{id}'. Ids are not case-sensitive."));
        continue;
      }

      if (!IsImageType(image.ContentType)) {
        problems.Add(Structure(GamepackObjectTypes.Image, id, "The image content type must be image/png or image/jpeg."));
        continue;
      }

      var bytes = DecodeImage(image.DataBase64, id!, problems);
      if (bytes is not null) {
        decoded[id!] = bytes;
      }
    }
  }

  private static bool IsImageType(string? contentType) =>
    string.Equals(contentType, "image/png", StringComparison.OrdinalIgnoreCase)
    || string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase);

  private static byte[]? DecodeImage(string? data, string id, List<GamepackProblem> problems) {
    if (string.IsNullOrWhiteSpace(data)) {
      problems.Add(Structure(GamepackObjectTypes.Image, id, "The image has no data."));
      return null;
    }

    if (data.Length > MaxImageBase64Chars) {
      problems.Add(Structure(GamepackObjectTypes.Image, id, $"The image is larger than {MaxImageBytes.ToString(CultureInfo.InvariantCulture)} bytes."));
      return null;
    }

    byte[] bytes;
    try {
      bytes = Convert.FromBase64String(data);
    }
    catch (FormatException) {
      problems.Add(Structure(GamepackObjectTypes.Image, id, "The image data is not valid base64."));
      return null;
    }

    if (bytes.Length == 0) {
      problems.Add(Structure(GamepackObjectTypes.Image, id, "The image has no data."));
      return null;
    }

    if (bytes.Length > MaxImageBytes) {
      problems.Add(Structure(GamepackObjectTypes.Image, id, $"The image is larger than {MaxImageBytes.ToString(CultureInfo.InvariantCulture)} bytes."));
      return null;
    }

    return bytes;
  }

  // ---- conflicts and update decisions ----

  private sealed record Decision(string? ExistingId);

  private static Dictionary<string, Decision> DecideSequences(
      GamepackFile file, IReadOnlyList<CommandSequence> existing, GamepackOwnerResolver owners, string? packName, List<GamepackProblem> problems) {
    var result = new Dictionary<string, Decision>(StringComparer.OrdinalIgnoreCase);
    foreach (var sequence in file.Sequences) {
      if (string.IsNullOrWhiteSpace(sequence.Name) || result.ContainsKey(sequence.Name)) {
        continue;
      }

      var candidates = existing.Where(e => string.Equals(e.Name, sequence.Name, StringComparison.OrdinalIgnoreCase)).Select(e => e.Id);
      result[sequence.Name] = Decide(GamepackObjectTypes.Sequence, sequence.Name, candidates, owners, packName, problems);
    }

    return result;
  }

  private static Dictionary<string, Decision> DecideCommands(
      GamepackFile file, IReadOnlyList<Command> existing, GamepackOwnerResolver owners, string? packName, List<GamepackProblem> problems) {
    var result = new Dictionary<string, Decision>(StringComparer.OrdinalIgnoreCase);
    foreach (var command in file.Commands) {
      if (string.IsNullOrWhiteSpace(command.Name) || result.ContainsKey(command.Name)) {
        continue;
      }

      var candidates = existing.Where(e => string.Equals(e.Name, command.Name, StringComparison.OrdinalIgnoreCase)).Select(e => e.Id);
      result[command.Name] = Decide(GamepackObjectTypes.Command, command.Name, candidates, owners, packName, problems);
    }

    return result;
  }

  private static Dictionary<string, Decision> DecideImages(
      GamepackFile file, IReadOnlyCollection<string> existingIds, GamepackOwnerResolver owners, string? packName, List<GamepackProblem> problems) {
    var result = new Dictionary<string, Decision>(StringComparer.OrdinalIgnoreCase);
    foreach (var image in file.Images) {
      if (!ReferenceImageIdValidator.IsValid(image.Id) || result.ContainsKey(image.Id!)) {
        continue;
      }

      var candidates = existingIds.Where(e => string.Equals(e, image.Id, StringComparison.OrdinalIgnoreCase));
      result[image.Id!] = Decide(GamepackObjectTypes.Image, image.Id!, candidates, owners, packName, problems);
    }

    return result;
  }

  private static Decision Decide(string type, string name, IEnumerable<string> candidateIds, GamepackOwnerResolver owners, string? packName, List<GamepackProblem> problems) {
    string? updateId = null;
    foreach (var id in candidateIds) {
      var owner = owners.Resolve(type, id);
      if (!owner.IsUser && packName is not null && GamepackNameRules.Comparer.Equals(owner.Name, packName)) {
        updateId ??= id;
        continue;
      }

      problems.Add(new GamepackProblem(
        GamepackProblemCategories.Conflict,
        type,
        name,
        owner.IsUser
          ? $"A {type} with the name '{name}' exists and the user owns it."
          : $"A {type} with the name '{name}' exists and the gamepack '{owner.ToText()}' owns it.",
        owner.ToText()));
    }

    return new Decision(updateId);
  }

  // ---- removals ----

  private static List<PlannedRemoval> FindRemovals(
      GamepackRegistry registry,
      GamepackOwnerResolver owners,
      string? packName,
      IReadOnlyList<CommandSequence> sequences,
      IReadOnlyList<Command> commands,
      IReadOnlyCollection<string> imageIds,
      Dictionary<string, Decision> sequenceDecisions,
      Dictionary<string, Decision> commandDecisions,
      Dictionary<string, Decision> imageDecisions) {
    var removals = new List<PlannedRemoval>();
    if (packName is null) {
      return removals;
    }

    var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    AddKept(kept, GamepackObjectTypes.Sequence, sequenceDecisions);
    AddKept(kept, GamepackObjectTypes.Command, commandDecisions);
    AddKept(kept, GamepackObjectTypes.Image, imageDecisions);

    foreach (var pair in registry.Owners) {
      if (!GamepackNameRules.Comparer.Equals(pair.Value.Gamepack, packName)) {
        continue;
      }

      var type = GamepackRegistry.TypeOfKey(pair.Key);
      var id = pair.Value.Id;
      if (kept.Contains(type + "|" + id)) {
        continue;
      }

      var name = type switch {
        GamepackObjectTypes.Sequence => sequences.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))?.Name,
        GamepackObjectTypes.Command => commands.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))?.Name,
        GamepackObjectTypes.Image => imageIds.FirstOrDefault(i => string.Equals(i, id, StringComparison.OrdinalIgnoreCase)),
        _ => null
      };

      // An owner entry whose object does not exist is not an object (FR-005). Nothing to remove.
      if (name is not null && owners.Find(type, id) is not null) {
        removals.Add(new PlannedRemoval(type, name, id));
      }
    }

    return removals
      .OrderBy(r => RemovalRank(r.Type))
      .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
      .ToList();
  }

  private static int RemovalRank(string type) => type switch {
    GamepackObjectTypes.Sequence => 0,
    GamepackObjectTypes.Command => 1,
    _ => 2
  };

  private static void AddKept(HashSet<string> kept, string type, Dictionary<string, Decision> decisions) {
    foreach (var decision in decisions.Values) {
      if (decision.ExistingId is not null) {
        kept.Add(type + "|" + decision.ExistingId);
      }
    }
  }

  // ---- references ----

  private static void CheckReferences(
      GamepackFile file,
      IReadOnlyList<Command> existingCommands,
      IReadOnlyCollection<string> existingImageIds,
      List<PlannedRemoval> removals,
      List<GamepackProblem> problems) {
    var commandTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var command in file.Commands) {
      if (!string.IsNullOrWhiteSpace(command.Id)) {
        commandTargets.Add(command.Id);
      }
    }

    var removedCommands = removals.Where(r => r.Type == GamepackObjectTypes.Command).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var existing in existingCommands) {
      if (!removedCommands.Contains(existing.Id)) {
        commandTargets.Add(existing.Id);
      }
    }

    var imageTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var image in file.Images) {
      if (!string.IsNullOrWhiteSpace(image.Id)) {
        imageTargets.Add(image.Id);
      }
    }

    var removedImages = removals.Where(r => r.Type == GamepackObjectTypes.Image).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var id in existingImageIds) {
      if (!removedImages.Contains(id)) {
        imageTargets.Add(id);
      }
    }

    foreach (var sequence in file.Sequences) {
      ReportMissing(problems, GamepackObjectTypes.Sequence, sequence.Name, "Command", GamepackReferences.CommandIds(sequence), commandTargets);
      ReportMissing(problems, GamepackObjectTypes.Sequence, sequence.Name, "Image", GamepackReferences.ImageIds(sequence), imageTargets);
    }

    foreach (var command in file.Commands) {
      ReportMissing(problems, GamepackObjectTypes.Command, command.Name, "Command", GamepackReferences.CommandIds(command), commandTargets);
      ReportMissing(problems, GamepackObjectTypes.Command, command.Name, "Image", GamepackReferences.ImageIds(command), imageTargets);
    }
  }

  private static void ReportMissing(List<GamepackProblem> problems, string type, string name, string targetKind, IEnumerable<string> references, HashSet<string> targets) {
    foreach (var reference in references.Distinct(StringComparer.OrdinalIgnoreCase)) {
      if (!targets.Contains(reference)) {
        problems.Add(new GamepackProblem(
          GamepackProblemCategories.Reference,
          type,
          name,
          $"{targetKind} '{reference}' is not in the gamepack or in the installation."));
      }
    }
  }

  // ---- plan ----

  private static GamepackPlan BuildPlan(
      GamepackFile file,
      IReadOnlyList<CommandSequence> existingSequences,
      IReadOnlyList<Command> existingCommands,
      Dictionary<string, Decision> sequenceDecisions,
      Dictionary<string, Decision> commandDecisions,
      Dictionary<string, Decision> imageDecisions,
      Dictionary<string, byte[]> decoded,
      List<PlannedRemoval> removals) {
    var commandUsed = new HashSet<string>(existingCommands.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);
    var sequenceUsed = new HashSet<string>(existingSequences.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

    // Final command ids come first, so that every reference can be rewritten once.
    var commandIdMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var commandWrites = new List<PlannedObject>();
    foreach (var command in file.Commands) {
      var existingId = commandDecisions[command.Name].ExistingId;
      var finalId = existingId ?? PickNewId(command.Id, commandUsed);
      if (!string.IsNullOrWhiteSpace(command.Id)) {
        commandIdMap[command.Id] = finalId;
      }

      command.Id = finalId;
      commandWrites.Add(new PlannedObject { Type = GamepackObjectTypes.Command, Name = command.Name, FinalId = finalId, ExistingId = existingId, Command = command });
    }

    string? Map(string id) => commandIdMap.TryGetValue(id, out var mapped) ? mapped : null;
    foreach (var write in commandWrites) {
      GamepackReferences.RewriteCommandIds(write.Command!, Map);
    }

    var sequenceWrites = new List<PlannedObject>();
    foreach (var sequence in file.Sequences) {
      var existingId = sequenceDecisions[sequence.Name].ExistingId;
      var finalId = existingId ?? PickNewId(sequence.Id, sequenceUsed);
      sequence.Id = finalId;
      GamepackReferences.RewriteCommandIds(sequence, Map);
      sequenceWrites.Add(new PlannedObject { Type = GamepackObjectTypes.Sequence, Name = sequence.Name, FinalId = finalId, ExistingId = existingId, Sequence = sequence });
    }

    var imageWrites = new List<PlannedObject>();
    foreach (var image in file.Images) {
      var existingId = imageDecisions[image.Id!].ExistingId;
      imageWrites.Add(new PlannedObject {
        Type = GamepackObjectTypes.Image,
        Name = image.Id!,
        FinalId = existingId ?? image.Id!,
        ExistingId = existingId,
        Image = image,
        ImageBytes = decoded[image.Id!]
      });
    }

    var writes = new List<PlannedObject>();
    writes.AddRange(imageWrites);
    writes.AddRange(commandWrites);
    writes.AddRange(sequenceWrites);
    return new GamepackPlan { Manifest = file.Manifest!, Writes = writes, Removals = removals };
  }

  // A new object keeps the file id when the id is safe and free. Otherwise it gets a new id. A new object never
  // takes the id of a stored object, so no install can overwrite an object that it does not own.
  private static string PickNewId(string? fileId, HashSet<string> used) {
    string id;
    if (!string.IsNullOrWhiteSpace(fileId) && IsSafeId(fileId) && !used.Contains(fileId)) {
      id = fileId;
    }
    else {
      do {
        id = Guid.NewGuid().ToString("N");
      } while (used.Contains(id));
    }

    used.Add(id);
    return id;
  }

  private static bool IsSafeId(string id) {
    foreach (var c in id) {
      if (!(char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-')) {
        return false;
      }
    }

    return true;
  }

  private static GamepackProblem Structure(string type, string? name, string message) =>
    new(GamepackProblemCategories.Structure, type, name, message);
}
