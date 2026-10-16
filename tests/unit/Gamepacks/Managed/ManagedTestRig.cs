using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.Domain.Games;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueSettings;
using GameBot.Domain.QueueTemplates;

#pragma warning disable CA2007, CA1707, CA1002, CA1819, CA2227, CA1822, CA1812, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks.Managed;

/// <summary>A template store that can fail an update. It wraps the real store.</summary>
internal sealed class FaultingTemplateRepository : IQueueTemplateRepository {
  private readonly IQueueTemplateRepository _inner;

  public FaultingTemplateRepository(IQueueTemplateRepository inner) {
    _inner = inner;
  }

  /// <summary>When true, each update throws.</summary>
  public bool FailUpdate { get; set; }

  /// <summary>The number of update calls.</summary>
  public int Updates { get; private set; }

  public Task<QueueTemplate?> GetAsync(string id) => _inner.GetAsync(id);

  public Task<IReadOnlyList<QueueTemplate>> ListAsync() => _inner.ListAsync();

  public Task<QueueTemplate?> FindByNameAsync(string name) => _inner.FindByNameAsync(name);

  public Task<QueueTemplate> CreateAsync(QueueTemplate item) => _inner.CreateAsync(item);

  public Task<QueueTemplate> UpdateAsync(QueueTemplate item) {
    Updates++;
    if (FailUpdate) {
      throw new InvalidOperationException("Injected template write fault.");
    }

    return _inner.UpdateAsync(item);
  }

  public Task<bool> DeleteAsync(string id) => _inner.DeleteAsync(id);
}

/// <summary>
/// Real file-backed stores in a temporary folder, with the managed update orchestrator on top and a fake queue port
/// (feature 148). The gamepack service has a signature checker, so a test can set the policy.
/// </summary>
internal sealed class ManagedTestRig : IDisposable {
  public ManagedTestRig(TimeSpan? stopTimeout = null) {
    Packs = new GamepackTestKit();
    Queues = new FileQueueRepository(Packs.Root);
    Runtime = new QueueRuntimeStore();
    BaseTemplates = new FileQueueTemplateRepository(Packs.Root);
    Templates = new FaultingTemplateRepository(BaseTemplates);
    Games = new FileGameRepository(Packs.Root);
    SignatureSettings = new GamepackSignatureSettingsStore(Packs.Root);
    var checker = new GamepackSignatureChecker(SignatureSettings, new GamepackSignatureVerifier());
    Service = new GamepackService(Packs.Sequences, Packs.Commands, Packs.Images, Packs.Store, Packs.Validator, Packs.Applier, signatureChecker: checker);
    Exporter = new QueueSettingsExporter(Packs.Sequences, Packs.Commands, Queues, Runtime, BaseTemplates, Games, Service);
    Importer = new QueueSettingsImporter(Packs.Sequences, Packs.Commands, Queues, Runtime, BaseTemplates, Games, Packs.Images, Service);
    Control = new FakeManagedQueueControl(Queues);
    Backups = new ManagedBackupStore(Packs.Root);
    Options = new ManagedUpdateOptions { StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(5) };
    Orchestrator = new GamepackUpdateOrchestrator(Service, Control, Templates, Exporter, Importer, Backups, Options);
  }

  public GamepackTestKit Packs { get; }
  public FileQueueRepository Queues { get; }
  public QueueRuntimeStore Runtime { get; }
  public FileQueueTemplateRepository BaseTemplates { get; }
  public FaultingTemplateRepository Templates { get; }
  public FileGameRepository Games { get; }
  public GamepackSignatureSettingsStore SignatureSettings { get; }
  public GamepackService Service { get; }
  public QueueSettingsExporter Exporter { get; }
  public QueueSettingsImporter Importer { get; }
  public FakeManagedQueueControl Control { get; }
  public ManagedBackupStore Backups { get; }
  public ManagedUpdateOptions Options { get; }
  public GamepackUpdateOrchestrator Orchestrator { get; }

  public string Root => Packs.Root;

  public void Dispose() {
    Service.Dispose();
    Packs.Dispose();
  }

  // ---- builders ----

  /// <summary>A gamepack file with one command and one sequence for each name. Sequence ids are <c>id-&lt;name&gt;</c>.</summary>
  public static string Pack(string version, params string[] sequenceNames) => PackNamed("pns", version, sequenceNames);

  public static string PackNamed(string name, string version, params string[] sequenceNames) =>
    GamepackTestKit.Pack(
      name,
      version,
      sequences: sequenceNames.Select(n => GamepackTestKit.Seq("id-" + n, n, "c-main")).ToArray(),
      commands: new[] { GamepackTestKit.Cmd("c-main", "MainCmd-" + name) });

  public async Task<GamepackInstallOutcome> InstallAsync(string version, params string[] sequenceNames) {
    var outcome = await Service.InstallAsync(Pack(version, sequenceNames), dryRun: false);
    if (outcome.Status != GamepackInstallStatus.Ok) {
      throw new InvalidOperationException("The test install failed: " + outcome.Status);
    }

    return outcome;
  }

  public async Task<string> SequenceIdAsync(string name) => (await Packs.SequenceByNameAsync(name))!.Id;

  /// <summary>Makes a template whose entries refer to the named sequences. Each entry is enabled.</summary>
  public async Task<QueueTemplate> AddTemplateAsync(string name, params string[] sequenceNames) {
    var template = new QueueTemplate { Id = Guid.NewGuid().ToString("N"), Name = name };
    foreach (var sequence in sequenceNames) {
      template.Entries.Add(new QueueTemplateEntry { SequenceId = await SequenceIdAsync(sequence) });
    }

    return await BaseTemplates.CreateAsync(template);
  }

  /// <summary>Makes a queue that links to a template. When <paramref name="running"/> is true, the fake marks it as running.</summary>
  public async Task<ExecutionQueue> AddQueueAsync(string name, QueueTemplate? template, bool running) {
    var queue = await Queues.CreateAsync(new ExecutionQueue {
      Id = Guid.NewGuid().ToString("N"),
      Name = name,
      EmulatorSerial = "emulator-5554",
      LinkedTemplateId = template?.Id
    });
    if (running) {
      Control.Running.Add(queue.Id);
    }

    return queue;
  }

  public static ManagedUpdateRequest Update(string fileText, bool dryRun = false, string? signature = null, params string[] templates) => new() {
    Mode = ManagedUpdateMode.Update,
    FileText = fileText,
    SignatureText = signature,
    DryRun = dryRun,
    Templates = templates.Length == 0 ? null : templates
  };

  public static ManagedUpdateRequest Rollback(string name, bool dryRun = false, params string[] templates) => new() {
    Mode = ManagedUpdateMode.Rollback,
    GamepackName = name,
    DryRun = dryRun,
    Templates = templates.Length == 0 ? null : templates
  };

  /// <summary>A short text of a report for an assertion message.</summary>
  public static string Describe(ManagedUpdateReport report) =>
    $"result={report.Result} reason={report.Reason} detail={report.Detail} problems=[{string.Join("; ", report.Problems.Select(p => p.Category + ":" + p.Message))}] restore=[{string.Join(",", report.Restore.Steps.Select(s => s.Step + "=" + s.Outcome + " " + s.Error))}]";

  // ---- reads ----

  public async Task<QueueTemplate> TemplateAsync(string name) => (await BaseTemplates.FindByNameAsync(name))!;

  public Task<ManagedUpdateReport> RunAsync(ManagedUpdateRequest request) => Orchestrator.RunAsync(request);

  /// <summary>A text form of the state that a call must not change in a failed or dry call: gamepack list and template entries.</summary>
  public async Task<string> StateAsync() {
    var packs = string.Join(",", Service.List().Select(g => g.Name + "@" + g.Version + "/" + g.HasPreviousVersion));
    var templates = string.Join(
      ";",
      (await BaseTemplates.ListAsync()).OrderBy(t => t.Name, StringComparer.Ordinal).Select(t =>
        t.Name + ":" + string.Join("|", t.Entries.Select(e => e.SequenceId + "/" + e.Enabled + "/" + e.ScheduleType))));
    var sequences = string.Join(",", (await Packs.Sequences.ListAsync()).Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));
    var running = string.Join(",", Control.Running.OrderBy(i => i, StringComparer.Ordinal));
    return packs + "#" + templates + "#" + sequences + "#" + running;
  }
}
