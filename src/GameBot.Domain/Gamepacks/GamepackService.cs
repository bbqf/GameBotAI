using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks.Seeds;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.Domain.Images;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Gamepacks;

/// <summary>The status of an install request.</summary>
public enum GamepackInstallStatus {
  /// <summary>The file is valid. For a real install, the install worked.</summary>
  Ok,

  /// <summary>The check found problems. Nothing changed.</summary>
  Invalid,

  /// <summary>The body is not valid JSON.</summary>
  InvalidJson,

  /// <summary>The signature policy rejected the gamepack. Nothing changed (feature 146).</summary>
  SignatureRejected,

  /// <summary>A write failed and the state is as before.</summary>
  Failed,

  /// <summary>A write failed and the restore failed too.</summary>
  PartialRestore
}

/// <summary>The result of an install request or a dry run.</summary>
public sealed class GamepackInstallOutcome {
  /// <summary>The status.</summary>
  public GamepackInstallStatus Status { get; init; }

  /// <summary>True for a dry run.</summary>
  public bool DryRun { get; init; }

  /// <summary>All problems.</summary>
  public IReadOnlyList<GamepackProblem> Problems { get; init; } = Array.Empty<GamepackProblem>();

  /// <summary>The gamepack name, when known.</summary>
  public string? Name { get; init; }

  /// <summary>The gamepack version, when known.</summary>
  public string? Version { get; init; }

  /// <summary>The number of created objects (planned for a dry run).</summary>
  public int Created { get; init; }

  /// <summary>The number of updated objects (planned for a dry run).</summary>
  public int Updated { get; init; }

  /// <summary>The number of removed objects (planned for a dry run).</summary>
  public int Removed { get; init; }

  /// <summary>The error text for a failed write, or null.</summary>
  public string? ErrorMessage { get; init; }

  /// <summary>The number of user objects taken over (planned for a dry run). 0 when the option is off (feature 149).</summary>
  public int TakenOver { get; init; }

  /// <summary>The id of the take-over backup. Set only for a real install that replaced at least one object.</summary>
  public string? TakeOverBackupId { get; init; }

  /// <summary>The list of objects to take over. Set for a dry run with the option on and no problem. Null otherwise.</summary>
  public IReadOnlyList<TakeOverEntry>? TakeOvers { get; init; }

  /// <summary>The signature check result, or null when the service has no checker (feature 146).</summary>
  public SignatureCheckResult? Signature { get; init; }

  /// <summary>The names of the seed templates created (planned for a dry run). Always set (feature 150).</summary>
  public IReadOnlyList<string> SeedTemplatesCreated { get; init; } = Array.Empty<string>();

  /// <summary>The seed templates not created (planned for a dry run). Always set (feature 150).</summary>
  public IReadOnlyList<SeedSkip> SeedTemplatesSkipped { get; init; } = Array.Empty<SeedSkip>();
}

/// <summary>The status of a rollback request.</summary>
public enum GamepackRollbackStatus {
  /// <summary>The rollback worked.</summary>
  RolledBack,

  /// <summary>The check found problems. Nothing changed.</summary>
  Refused,

  /// <summary>The gamepack has no kept previous version.</summary>
  NoPreviousVersion,

  /// <summary>No gamepack has this name.</summary>
  NotFound,

  /// <summary>A write failed and the state is as before.</summary>
  Failed,

  /// <summary>A write failed and the restore failed too.</summary>
  PartialRestore
}

/// <summary>The result of a rollback request.</summary>
public sealed class GamepackRollbackOutcome {
  /// <summary>The status.</summary>
  public GamepackRollbackStatus Status { get; init; }

  /// <summary>The problems of a refused rollback.</summary>
  public IReadOnlyList<GamepackProblem> Problems { get; init; } = Array.Empty<GamepackProblem>();

  /// <summary>The gamepack name.</summary>
  public string? Name { get; init; }

  /// <summary>The version after the rollback.</summary>
  public string? Version { get; init; }

  /// <summary>The number of created objects.</summary>
  public int Created { get; init; }

  /// <summary>The number of updated objects.</summary>
  public int Updated { get; init; }

  /// <summary>The number of removed objects.</summary>
  public int Removed { get; init; }

  /// <summary>The error text for a failed write, or null.</summary>
  public string? ErrorMessage { get; init; }
}

/// <summary>One row of the installed gamepack list.</summary>
/// <param name="Name">The gamepack name.</param>
/// <param name="Version">The version.</param>
/// <param name="Changelog">The change text.</param>
/// <param name="RequiredApiMajor">The optional API major version.</param>
/// <param name="HasPreviousVersion">True when a previous version is kept.</param>
/// <param name="InstalledAtUtc">The time of the install.</param>
public sealed record GamepackListItem(
    string Name,
    string Version,
    string Changelog,
    int? RequiredApiMajor,
    bool HasPreviousVersion,
    DateTimeOffset InstalledAtUtc);

/// <summary>
/// The gamepack service (feature 143): install, dry run, rollback, list, and owner lookup. One gate lets only one
/// install or rollback run at a time. A hand create, edit, or delete does not take the gate (FR-015).
/// </summary>
public sealed partial class GamepackService : IDisposable {
  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly ISequenceRepository _sequences;
  private readonly ICommandRepository _commands;
  private readonly IImageRepository _images;
  private readonly IGamepackRegistryStore _store;
  private readonly GamepackValidator _validator;
  private readonly GamepackApplier _applier;
  private readonly ILogger _logger;
  private readonly GamepackSignatureChecker? _signatureChecker;

  /// <summary>Creates the service.</summary>
  /// <param name="sequences">The sequence store.</param>
  /// <param name="commands">The command store.</param>
  /// <param name="images">The image store.</param>
  /// <param name="store">The registry store.</param>
  /// <param name="validator">The shared check.</param>
  /// <param name="applier">The apply path.</param>
  /// <param name="logger">The logger, or null.</param>
  /// <param name="signatureChecker">The signature checker, or null for no signature check (feature 146).</param>
  public GamepackService(
      ISequenceRepository sequences,
      ICommandRepository commands,
      IImageRepository images,
      IGamepackRegistryStore store,
      GamepackValidator validator,
      GamepackApplier applier,
      ILogger<GamepackService>? logger = null,
      GamepackSignatureChecker? signatureChecker = null) {
    _sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
    _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    _images = images ?? throw new ArgumentNullException(nameof(images));
    _store = store ?? throw new ArgumentNullException(nameof(store));
    _validator = validator ?? throw new ArgumentNullException(nameof(validator));
    _applier = applier ?? throw new ArgumentNullException(nameof(applier));
    _logger = (ILogger?)logger ?? NullLogger.Instance;
    _signatureChecker = signatureChecker;
  }

  /// <inheritdoc />
  public void Dispose() => _gate.Dispose();

  /// <summary>Installs a gamepack file, or checks it when <paramref name="dryRun"/> is true.</summary>
  /// <param name="json">The file text.</param>
  /// <param name="dryRun">True to check only. No data changes.</param>
  /// <param name="ct">The cancellation token.</param>
  public Task<GamepackInstallOutcome> InstallAsync(string json, bool dryRun, CancellationToken ct = default) =>
    InstallAsync(json, dryRun, signatureText: null, ct);

  /// <summary>
  /// Installs a gamepack file with a detached signature, or checks it when <paramref name="dryRun"/> is true (feature 146).
  /// The signature check ends before the validator and before any write.
  /// </summary>
  /// <param name="json">The file text.</param>
  /// <param name="dryRun">True to check only. No data changes.</param>
  /// <param name="signatureText">The detached signature file text, or null when none was sent.</param>
  /// <param name="ct">The cancellation token.</param>
  public Task<GamepackInstallOutcome> InstallAsync(string json, bool dryRun, string? signatureText, CancellationToken ct) =>
    InstallAsync(json, new GamepackInstallOptions(dryRun, TakeOver: false, signatureText), ct);

  /// <summary>
  /// Installs a gamepack file, or checks it when the option <see cref="GamepackInstallOptions.DryRun"/> is true (feature 149).
  /// With <see cref="GamepackInstallOptions.TakeOver"/> on, a user object of the same type and name is taken over: the
  /// gamepack owns it and the id stays. The old content goes to one take-over backup before the first write.
  /// </summary>
  /// <param name="json">The file text.</param>
  /// <param name="options">The options of the request.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<GamepackInstallOutcome> InstallAsync(string json, GamepackInstallOptions options, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(json);
    ArgumentNullException.ThrowIfNull(options);
    var dryRun = options.DryRun;
    var signatureText = options.SignatureText;
    var parsed = GamepackFileSerializer.Parse(json);
    if (parsed.InvalidJson || parsed.File is null) {
      return new GamepackInstallOutcome { Status = GamepackInstallStatus.InvalidJson, DryRun = dryRun, Problems = parsed.Problems };
    }

    // The signature check ends before the gate, the validator, and any write (FR-002).
    var signature = _signatureChecker?.Check(json, signatureText);
    if (signature is { WouldReject: true }) {
      var manifestInfo = parsed.File.Manifest;
      var status = signature.Status.ToWire();
      return new GamepackInstallOutcome {
        Status = GamepackInstallStatus.SignatureRejected,
        DryRun = dryRun,
        Name = manifestInfo?.Name,
        Version = manifestInfo?.Version,
        Signature = signature,
        Problems = new[] {
          new GamepackProblem(
            GamepackProblemCategories.Signature,
            GamepackObjectTypes.Manifest,
            manifestInfo?.Name,
            $"The signature check gave the status {status}. The policy is required. Nothing changed.",
            Code: "signature_" + status)
        }
      };
    }

    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var check = await _validator.ValidateAsync(parsed.File, parsed.Problems, ct, takeOver: options.TakeOver, seeds: true).ConfigureAwait(false);
      var manifest = parsed.File.Manifest;
      if (!check.IsValid || check.Plan is null) {
        return new GamepackInstallOutcome {
          Signature = signature,
          Status = GamepackInstallStatus.Invalid,
          DryRun = dryRun,
          Problems = check.Problems,
          Name = manifest?.Name,
          Version = manifest?.Version
        };
      }

      var plan = check.Plan;
      if (dryRun) {
        return new GamepackInstallOutcome {
          Signature = signature,
          Status = GamepackInstallStatus.Ok,
          DryRun = true,
          Name = plan.Manifest.Name,
          Version = plan.Manifest.Version,
          Created = plan.Created,
          Updated = plan.Updated,
          Removed = plan.Removed,
          TakenOver = plan.TakenOver,
          TakeOvers = options.TakeOver ? plan.TakeOvers : null,
          SeedTemplatesCreated = plan.Seeds.Items.Where(i => i.Outcome == SeedOutcomes.Create).Select(i => i.Name).ToList(),
          SeedTemplatesSkipped = plan.Seeds.Items.Where(i => i.Outcome == SeedOutcomes.SkipExists).Select(i => new SeedSkip(i.Name, SeedSkipReasons.Exists)).ToList()
        };
      }

      var record = _store.Load().Gamepacks.GetValueOrDefault(GamepackNameRules.Key(plan.Manifest.Name!));
      var previous = record is not null && !string.Equals(record.Version, plan.Manifest.Version, StringComparison.Ordinal)
        ? GamepackPreviousHandling.Capture
        : GamepackPreviousHandling.Keep;

      var applied = await _applier.ApplyAsync(plan, previous, ct).ConfigureAwait(false);
      if (applied.Status != GamepackApplyStatus.Applied) {
        return new GamepackInstallOutcome {
          Signature = signature,
          Status = applied.Status == GamepackApplyStatus.PartialRestore ? GamepackInstallStatus.PartialRestore : GamepackInstallStatus.Failed,
          Name = plan.Manifest.Name,
          Version = plan.Manifest.Version,
          ErrorMessage = applied.Error?.Message
        };
      }

      return new GamepackInstallOutcome {
        Signature = signature,
        Status = GamepackInstallStatus.Ok,
        Name = plan.Manifest.Name,
        Version = plan.Manifest.Version,
        Created = applied.Created,
        Updated = applied.Updated,
        Removed = applied.Removed,
        TakenOver = applied.TakenOver,
        TakeOverBackupId = applied.TakeOverBackupId,
        SeedTemplatesCreated = applied.SeedTemplatesCreated,
        SeedTemplatesSkipped = plan.Seeds.Items
          .Where(i => i.Outcome == SeedOutcomes.SkipExists)
          .Select(i => new SeedSkip(i.Name, SeedSkipReasons.Exists))
          .Concat(applied.SeedTemplatesSkipped)
          .ToList()
      };
    }
    finally {
      _gate.Release();
    }
  }

  /// <summary>
  /// Puts back the objects of a take-over backup (feature 149). The restore checks <c>object_missing</c>, <c>object_changed</c>,
  /// and <c>owner_changed</c>. It holds the install gate. It does not stop or start a queue. The public route uses this method only.
  /// </summary>
  /// <param name="backupId">The backup id.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<TakeOverRestoreOutcome> RestoreTakeOverAsync(string backupId, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(backupId);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      return await _applier.RestoreTakeOverAsync(backupId, TakeOverRestoreMode.Strict, ct).ConfigureAwait(false);
    }
    finally {
      _gate.Release();
    }
  }

  /// <summary>
  /// Puts back the objects of a take-over backup and skips the owner check (feature 149). Only the managed update calls it, and it
  /// runs inside the exclusive scope of <see cref="RunExclusiveAsync{T}"/>. This method does not take the gate a second time.
  /// No route uses it.
  /// </summary>
  /// <param name="backupId">The backup id.</param>
  /// <param name="ct">The cancellation token.</param>
  internal Task<TakeOverRestoreOutcome> RestoreTakeOverForcedInGateAsync(string backupId, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(backupId);
    return _applier.RestoreTakeOverAsync(backupId, TakeOverRestoreMode.Force, ct);
  }

  /// <summary>Rolls a gamepack back to its kept previous version.</summary>
  /// <param name="name">The gamepack name. Case is ignored.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<GamepackRollbackOutcome> RollbackAsync(string name, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(name);
    if (!GamepackNameRules.IsValid(name)) {
      return new GamepackRollbackOutcome { Status = GamepackRollbackStatus.NotFound, Name = name };
    }

    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var registry = _store.Load();
      if (!registry.Gamepacks.TryGetValue(GamepackNameRules.Key(name), out var record)) {
        return new GamepackRollbackOutcome { Status = GamepackRollbackStatus.NotFound, Name = name };
      }

      var text = record.Previous is null ? null : _store.ReadPrevious(record.Name);
      if (record.Previous is null || text is null) {
        return new GamepackRollbackOutcome { Status = GamepackRollbackStatus.NoPreviousVersion, Name = record.Name, Version = record.Version };
      }

      var parsed = GamepackFileSerializer.Parse(text);
      if (parsed.File is null) {
        return new GamepackRollbackOutcome { Status = GamepackRollbackStatus.Refused, Name = record.Name, Problems = parsed.Problems };
      }

      // A rollback restores kept content that the install accepted before. It does not check the API major version.
      var check = await _validator.ValidateAsync(parsed.File, parsed.Problems, ct, checkApiVersion: false).ConfigureAwait(false);
      if (!check.IsValid || check.Plan is null) {
        return new GamepackRollbackOutcome { Status = GamepackRollbackStatus.Refused, Name = record.Name, Version = record.Version, Problems = check.Problems };
      }

      var applied = await _applier.ApplyAsync(check.Plan, GamepackPreviousHandling.Clear, ct).ConfigureAwait(false);
      if (applied.Status != GamepackApplyStatus.Applied) {
        return new GamepackRollbackOutcome {
          Status = applied.Status == GamepackApplyStatus.PartialRestore ? GamepackRollbackStatus.PartialRestore : GamepackRollbackStatus.Failed,
          Name = record.Name,
          Version = record.Version,
          ErrorMessage = applied.Error?.Message
        };
      }

      return new GamepackRollbackOutcome {
        Status = GamepackRollbackStatus.RolledBack,
        Name = record.Name,
        Version = check.Plan.Manifest.Version,
        Created = applied.Created,
        Updated = applied.Updated,
        Removed = applied.Removed
      };
    }
    finally {
      _gate.Release();
    }
  }

  /// <summary>
  /// Runs an action while it holds the gate of this service (feature 145, FR-017). An install, a rollback, and this
  /// action run one at a time. The behavior of install, rollback, and list does not change.
  /// </summary>
  /// <typeparam name="T">The result type of the action.</typeparam>
  /// <param name="action">The action to run.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(action);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      return await action().ConfigureAwait(false);
    }
    finally {
      _gate.Release();
    }
  }

  /// <summary>Lists the installed gamepacks, ordered by name.</summary>
  public IReadOnlyList<GamepackListItem> List() =>
    _store.Load().Gamepacks.Values
      .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
      .Select(g => new GamepackListItem(g.Name, g.Version, g.Changelog, g.RequiredApiMajor, g.Previous is not null, g.InstalledAtUtc))
      .ToList();

  /// <summary>Reads the registry once and gives an owner resolver for a whole request.</summary>
  public GamepackOwnerResolver LoadOwners() => new(_store.Load());

  /// <summary>
  /// Removes the owner entry of a deleted object at once (FR-015). A failure is logged and does not fail the delete.
  /// </summary>
  /// <param name="type">One of <see cref="GamepackObjectTypes"/>.</param>
  /// <param name="id">The stored id of the deleted object.</param>
  public void RemoveOwner(string type, string id) {
    ArgumentNullException.ThrowIfNull(type);
    ArgumentNullException.ThrowIfNull(id);
    try {
      var registry = _store.Load();
      if (registry.Owners.Any(p => IsEntryOf(p, type, id))) {
        _store.Update(r => {
          foreach (var key in r.Owners.Where(p => IsEntryOf(p, type, id)).Select(p => p.Key).ToList()) {
            r.Owners.Remove(key);
          }
        });
      }
    }
    catch (Exception ex) {
      LogRemoveOwnerFailed(_logger, type, ex);
    }
  }

  /// <summary>
  /// True when an existing object of this type with this name is owned by a gamepack. A hand create with this name
  /// is refused (US2 scenario 5).
  /// </summary>
  /// <param name="type">One of <see cref="GamepackObjectTypes"/>.</param>
  /// <param name="name">The object name. For an image, the id.</param>
  /// <param name="ct">The cancellation token.</param>
  public async Task<bool> IsNameOwnedAsync(string type, string name, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(type);
    if (string.IsNullOrWhiteSpace(name)) {
      return false;
    }

    var registry = _store.Load();
    if (registry.Owners.Count == 0) {
      return false;
    }

    var owners = new GamepackOwnerResolver(registry);
    IEnumerable<string> ids = type switch {
      GamepackObjectTypes.Sequence => (await _sequences.ListAsync().ConfigureAwait(false))
        .Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).Select(s => s.Id).ToList(),
      GamepackObjectTypes.Command => (await _commands.ListAsync(ct).ConfigureAwait(false))
        .Where(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)).Select(c => c.Id).ToList(),
      GamepackObjectTypes.Image => (await _images.ListIdsAsync(ct).ConfigureAwait(false))
        .Where(i => string.Equals(i, name, StringComparison.OrdinalIgnoreCase)).ToList(),
      _ => Array.Empty<string>()
    };

    return ids.Any(id => !owners.Resolve(type, id).IsUser);
  }

  private static bool IsEntryOf(KeyValuePair<string, OwnerEntry> pair, string type, string id) =>
    string.Equals(GamepackRegistry.TypeOfKey(pair.Key), type, StringComparison.OrdinalIgnoreCase)
    && string.Equals(pair.Value.Id, id, StringComparison.OrdinalIgnoreCase);

  [LoggerMessage(EventId = 1431, Level = LogLevel.Warning, Message = "The owner entry of a deleted {ObjectType} could not be removed. The owner read hides it.")]
  private static partial void LogRemoveOwnerFailed(ILogger logger, string objectType, Exception exception);
}
