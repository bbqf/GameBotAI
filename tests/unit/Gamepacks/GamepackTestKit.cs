using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Images;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Parameters;

#pragma warning disable CA2007, CA1861, CA1859, CA1707, CA1002, CA1819, CA2227, CA1416, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>A test gate (feature 143, T008). It throws at the Nth write call, or at every restore call.</summary>
internal sealed class ThrowingWriteGate : IGamepackWriteGate {
  private int _calls;

  /// <summary>The 1-based number of the write call that throws. 0 means never.</summary>
  public int ThrowAtCall { get; set; }

  /// <summary>When true, every restore call throws.</summary>
  public bool ThrowOnRestore { get; set; }

  /// <summary>The number of write calls so far. Restore calls are not counted.</summary>
  public int Calls => _calls;

  /// <summary>The log of all calls.</summary>
  public List<string> Log { get; } = new();

  /// <summary>Resets the call count and makes the Nth write call throw.</summary>
  public void Arm(int throwAtCall) {
    _calls = 0;
    ThrowAtCall = throwAtCall;
    Log.Clear();
  }

  public void BeforeWrite(string operation, string key) {
    Log.Add(operation + ":" + key);
    if (operation == GamepackWriteOperations.Restore) {
      if (ThrowOnRestore) {
        throw new InvalidOperationException("Injected restore fault at " + key);
      }

      return;
    }

    _calls++;
    if (ThrowAtCall != 0 && _calls == ThrowAtCall) {
      throw new InvalidOperationException("Injected write fault at call " + _calls);
    }
  }
}

/// <summary>Real file-backed stores in a temporary folder, with the gamepack services on top.</summary>
internal sealed class GamepackTestKit : IDisposable {
  public const string PngBase64 = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  public GamepackTestKit() {
    Root = Path.Combine(Path.GetTempPath(), "GameBotGamepack", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Root);
    Sequences = new FileSequenceRepository(Root);
    Commands = new FileCommandRepository(Root);
    Images = new FileImageRepository(Path.Combine(Root, "images"));
    Store = new GamepackRegistryStore(Root);
    Gate = new ThrowingWriteGate();
    Templates = new FileQueueTemplateRepository(Root);
    Validator = new GamepackValidator(Sequences, Commands, Images, Store, Templates);
    TakeOverBackups = new TakeOverBackupStore(Root);
    Alternates = new FileImageAlternatesRepository(Path.Combine(Root, "images"));
    Applier = new GamepackApplier(Sequences, Commands, Images, Store, Gate, takeOverBackups: TakeOverBackups, templates: Templates, alternates: Alternates);
    Service = new GamepackService(Sequences, Commands, Images, Store, Validator, Applier);
  }

  public string Root { get; }
  public FileSequenceRepository Sequences { get; }
  public FileCommandRepository Commands { get; }
  public FileImageRepository Images { get; }
  public FileImageAlternatesRepository Alternates { get; }
  public GamepackRegistryStore Store { get; }
  public TakeOverBackupStore TakeOverBackups { get; }
  public FileQueueTemplateRepository Templates { get; }
  public ThrowingWriteGate Gate { get; }
  public GamepackValidator Validator { get; }
  public GamepackApplier Applier { get; }
  public GamepackService Service { get; }

  public void Dispose() {
    Service.Dispose();
    try {
      Directory.Delete(Root, recursive: true);
    }
    catch (IOException) {
      // A temporary folder that stays is not a test failure.
    }
  }

  // ---- file builders ----

  public static object Seq(string id, string name, params string[] commandIds) => new {
    id,
    name,
    version = 1,
    steps = commandIds.Select((c, i) => new { order = i, stepId = "s" + i, commandId = c, stepType = "Command" }).ToArray()
  };

  public static object Cmd(string id, string name, string? imageId = null, string? callsCommandId = null) {
    var steps = new List<object>();
    if (imageId is not null) {
      steps.Add(new {
        type = "PrimitiveTap",
        order = 0,
        primitiveTap = new { detectionTarget = new { referenceImageId = imageId, confidence = 0.8, offsetX = 0, offsetY = 0, selectionStrategy = "HighestConfidence" } }
      });
    }

    if (callsCommandId is not null) {
      steps.Add(new { type = "Command", order = steps.Count, targetId = callsCommandId });
    }

    if (steps.Count == 0) {
      steps.Add(new { type = "KeyInput", order = 0, keyInput = new { key = "BACK" } });
    }

    return new { id, name, steps };
  }

  public static object Img(string id, string? data = null, string contentType = "image/png") => new { id, contentType, filename = id + ".png", dataBase64 = data ?? PngBase64 };

  /// <summary>An image entry with an alternates list (feature 153). A null list leaves the member out.</summary>
  public static object ImgAlt(string id, string[]? alternates, string? data = null) => alternates is null
    ? Img(id, data)
    : new { id, contentType = "image/png", filename = id + ".png", dataBase64 = data ?? PngBase64, alternates };

  /// <summary>Reads the stored alternates list of an image (feature 153).</summary>
  public IReadOnlyList<string> StoredAlternates(string id) => Alternates.GetAlternates(id);

  public static string Pack(string name, string version, object[]? sequences = null, object[]? commands = null, object[]? images = null, string changelog = "", object[]? seedTemplates = null)
    => JsonSerializer.Serialize(new Dictionary<string, object?> {
      ["manifest"] = new { name, version, changelog },
      ["sequences"] = sequences ?? Array.Empty<object>(),
      ["commands"] = commands ?? Array.Empty<object>(),
      ["images"] = images ?? Array.Empty<object>(),
      ["seedTemplates"] = seedTemplates
    }.Where(p => p.Key != "seedTemplates" || p.Value is not null).ToDictionary(p => p.Key, p => p.Value));

  /// <summary>A seed template with entries. Each entry names a sequence (feature 150).</summary>
  public static object Seed(string name, params object[] entries) => new { name, entries };

  /// <summary>A seed entry for the settings document shape (feature 150).</summary>
  public static object SeedEntry(string sequence, string scheduleType = "OncePerRun", bool enabled = true, string? timerTimeOfDay = null) =>
    timerTimeOfDay is null
      ? new { sequence, enabled, scheduleType }
      : (object)new { sequence, enabled, scheduleType, timerTimeOfDay };

  /// <summary>Adds a queue template by hand with the given name (feature 150).</summary>
  public async Task<QueueTemplate> AddTemplateAsync(string name) => await Templates.CreateAsync(new QueueTemplate { Name = name });

  /// <summary>Parses and checks a file text, as the service does.</summary>
  public async Task<GamepackValidationResult> CheckAsync(string json) {
    var parsed = GamepackFileSerializer.Parse(json);
    if (parsed.File is null) {
      return new GamepackValidationResult { Problems = parsed.Problems };
    }

    return await Validator.ValidateAsync(parsed.File, parsed.Problems);
  }

  /// <summary>Stores the objects of a pack text directly, with no owner. They are objects of the user (feature 149).</summary>
  public async Task AddUserObjectsAsync(string packJson) {
    var file = GamepackFileSerializer.Parse(packJson).File!;
    foreach (var image in file.Images) {
      using var stream = new MemoryStream(Convert.FromBase64String(image.DataBase64!));
      await Images.SaveAsync(image.Id!, stream, image.ContentType!, image.Filename, overwrite: true);
    }

    foreach (var command in file.Commands) {
      await Commands.AddAsync(command);
    }

    foreach (var sequence in file.Sequences) {
      await Sequences.CreateAsync(sequence);
    }
  }

  /// <summary>Installs a pack text with the take-over option and expects a result.</summary>
  public Task<GamepackInstallOutcome> InstallTakeOverAsync(string json, bool dryRun = false) =>
    Service.InstallAsync(json, new GamepackInstallOptions(dryRun, TakeOver: true));

  // ---- reads ----

  public async Task<CommandSequence?> SequenceByNameAsync(string name) =>
    (await Sequences.ListAsync()).FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

  public async Task<Command?> CommandByNameAsync(string name) =>
    (await Commands.ListAsync()).FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

  public GamepackOwner OwnerOf(string type, string id) => new GamepackOwnerResolver(Store.Load()).Resolve(type, id);

  /// <summary>A hash of every file under the data root, to compare two states byte by byte.</summary>
  public Dictionary<string, string> Snapshot() {
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) {
      if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) {
        continue;
      }

      result[Path.GetRelativePath(Root, file)] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    }

    return result;
  }
}
