using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using GameBot.Domain.Queues;
using GameBot.Domain.Updates;
using GameBot.Service.Hosted;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.Updates;
using GameBot.UnitTests.Updates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameBot.IntegrationTests.Updates;

/// <summary>
/// Feature 131: the check and the install run through the real client, downloader, store, and launcher.
/// Only the release server (a fake HTTP handler) and the process start are fakes.
/// </summary>
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Test helpers. The HttpClient only wraps a fake handler that holds no resources.")]
public sealed class UpdateFlowIntegrationTests : IDisposable {
  private const string ReleaseUrl = "https://api.github.com/repos/bbqf/GameBotAI/releases/latest";
  private const string ManifestUrl = "https://github.com/bbqf/GameBotAI/releases/download/v1.7.0.430/update-manifest.json";
  private const string MsiUrl = "https://github.com/bbqf/GameBotAI/releases/download/v1.7.0.430/GameBot.msi";

  private sealed class CapturingStarter : IUpdaterProcessStarter {
    public string? Exe { get; private set; }
    public IReadOnlyList<string>? Arguments { get; private set; }

    public void StartDetached(string exePath, IReadOnlyList<string> arguments, string workingDirectory) {
      Exe = exePath;
      Arguments = arguments;
    }
  }

  private readonly string _root = UpdateTestData.NewTempDirectory();
  private readonly byte[] _msi = Enumerable.Range(0, 20000).Select(i => (byte)(i * 7 % 253)).ToArray();
  private readonly CapturingStarter _starter = new();
  private readonly FakeLifetime _lifetime = new();
  private readonly FakeQueueStopper _queues = new();
  private readonly UpdatePaths _paths;
  private readonly string _installFolder;

  public UpdateFlowIntegrationTests() {
    _paths = new UpdatePaths(Path.Combine(_root, "data"));
    _installFolder = Path.Combine(_root, "app");
    Directory.CreateDirectory(Path.Combine(_installFolder, "updater"));
    File.WriteAllText(Path.Combine(_installFolder, "updater", "GameBot.Updater.exe"), "exe");
  }

  public void Dispose() {
    _lifetime.Dispose();
    Directory.Delete(_root, recursive: true);
    GC.SuppressFinalize(this);
  }

  private FakeHttpHandler ReleaseServer(string? sha = null) {
    var manifest = UpdateTestData.ManifestJson(size: _msi.Length, sha: sha ?? Convert.ToHexStringLower(SHA256.HashData(_msi)));
    return new FakeHttpHandler(request => request.RequestUri!.ToString() switch {
      ReleaseUrl => FakeHttpHandler.Json(UpdateTestData.ReleaseJson()),
      ManifestUrl => FakeHttpHandler.Json(manifest),
      MsiUrl => FakeHttpHandler.Redirect("https://release-assets.githubusercontent.com/token/GameBot.msi"),
      "https://release-assets.githubusercontent.com/token/GameBot.msi" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_msi) },
      _ => new HttpResponseMessage(HttpStatusCode.NotFound)
    });
  }

  private (UpdateCheckService Check, UpdateCoordinator Coordinator, UpdateResultStore Store) Build(FakeHttpHandler server) {
    var client = new GitHubReleaseClient(new HttpClient(server), Options.Create(new UpdateOptions()), NullLogger<GitHubReleaseClient>.Instance);
    var installed = new FakeInstalledVersion("1.7.0.412");
    var check = new UpdateCheckService(client, installed, TimeProvider.System, NullLogger<UpdateCheckService>.Instance);
    var store = new UpdateResultStore(_paths, NullLogger<UpdateResultStore>.Instance);
    var launcher = new UpdaterLauncher(_paths, _starter, new ConfigurationBuilder().Build(), _installFolder);
    var coordinator = new UpdateCoordinator(
      check,
      new UpdateDownloader(new HttpClient(server), _paths),
      launcher,
      store,
      installed,
      _queues,
      new FakeFreeSpace(),
      _paths,
      _lifetime,
      TimeProvider.System,
      NullLogger<UpdateCoordinator>.Instance);
    return (check, coordinator, store);
  }

  [Fact]
  public async Task CheckAndInstallRunTheWholeFlowAndTheNextStartReadsTheResult() {
    var (check, coordinator, store) = Build(ReleaseServer());
    using var _ = coordinator;

    var result = await check.CheckAsync(CancellationToken.None).ConfigureAwait(true);
    result.Status.Should().Be(UpdateCheckStatus.UpdateAvailable);
    var start = coordinator.TryStartInstall("1.7.0.430");
    await coordinator.Running.ConfigureAwait(true);

    start.Outcome.Should().Be(InstallStartOutcome.Started);
    coordinator.CurrentAttempt!.State.Should().Be(UpdateState.Installing);
    (await File.ReadAllBytesAsync(_paths.MsiPath("1.7.0.430")).ConfigureAwait(true)).Should().Equal(_msi);
    _lifetime.StopCalls.Should().Be(1);
    _queues.Calls.Should().Be(1);

    // The launcher got the right command line.
    _starter.Exe.Should().Be(Path.Combine(_paths.UpdaterCopyDirectory, "GameBot.Updater.exe"));
    var args = _starter.Arguments!;
    ValueAfter(args, "--msi").Should().Be(_paths.MsiPath("1.7.0.430"));
    ValueAfter(args, "--install-folder").Should().Be(_installFolder);
    ValueAfter(args, "--result-path").Should().Be(_paths.ResultFile);
    ValueAfter(args, "--target-version").Should().Be("1.7.0.430");
    ValueAfter(args, "--from-version").Should().Be("1.7.0.412");
    ValueAfter(args, "--attempt-id").Should().Be(start.Attempt!.AttemptId.ToString("D"));

    // The bot wrote the "installing" state before it exited.
    (await store.ReadAsync(CancellationToken.None).ConfigureAwait(true))!.State.Should().Be(UpdateState.Installing);

    // The updater program writes its final state. The new bot reads it at the next start.
    var written = await store.ReadAsync(CancellationToken.None).ConfigureAwait(true);
    await store.WriteAsync(written! with { State = UpdateState.Succeeded, FinishedAtUtc = DateTimeOffset.UtcNow, MsiexecExitCode = 0 }, CancellationToken.None).ConfigureAwait(true);
    var reporter = new UpdateResultReporter(store, new FakeInstalledVersion("1.7.0.430"), TimeProvider.System, NullLogger<UpdateResultReporter>.Instance);
    await reporter.LoadAsync(CancellationToken.None).ConfigureAwait(true);

    reporter.LastResult!.State.Should().Be(UpdateState.Succeeded);
    reporter.LastResult.AttemptId.Should().Be(start.Attempt.AttemptId);
    File.Exists(_paths.ResultFile).Should().BeFalse();
    File.Exists(_paths.ReportedResultFile).Should().BeTrue();
    File.Exists(_paths.MsiPath("1.7.0.430")).Should().BeFalse("a successful update removes the download");
  }

  [Fact]
  public async Task WrongChecksumStopsBeforeTheUpdaterAndLeavesNoFile() {
    var (check, coordinator, store) = Build(ReleaseServer(sha: new string('c', 64)));
    using var _ = coordinator;

    await check.CheckAsync(CancellationToken.None).ConfigureAwait(true);
    coordinator.TryStartInstall("1.7.0.430");
    await coordinator.Running.ConfigureAwait(true);

    coordinator.CurrentAttempt!.State.Should().Be(UpdateState.Failed);
    coordinator.CurrentAttempt.ErrorCode.Should().Be("update_checksum_mismatch");
    _starter.Exe.Should().BeNull();
    _lifetime.StopCalls.Should().Be(0);
    File.Exists(_paths.MsiPath("1.7.0.430")).Should().BeFalse();
    (await store.ReadAsync(CancellationToken.None).ConfigureAwait(true)).Should().BeNull();
  }

  [Fact]
  public async Task QueueThatOptsInStartsAgainAfterTheRestartThatTheUpdateCaused() {
    var (check, coordinator, _) = Build(ReleaseServer());
    using var _ = coordinator;
    using var runState = new FileQueueRunStateStore(_paths.DataRoot);
    await runState.MarkRunningAsync("q1").ConfigureAwait(true);
    await runState.MarkRunningAsync("q2").ConfigureAwait(true);

    await check.CheckAsync(CancellationToken.None).ConfigureAwait(true);
    coordinator.TryStartInstall("1.7.0.430");
    await coordinator.Running.ConfigureAwait(true);

    // The host was asked to stop before the queues stopped, so the queue engine keeps the records.
    _lifetime.ApplicationStopping.IsCancellationRequested.Should().BeTrue();

    // Simulated restart: a new resume service reads the same record.
    var queues = new FakeQueues();
    queues.Items.Add(new ExecutionQueue { Id = "q1", Name = "q1", EmulatorSerial = "emu-1", ResumeOnServiceStart = true });
    queues.Items.Add(new ExecutionQueue { Id = "q2", Name = "q2", EmulatorSerial = "emu-2", ResumeOnServiceStart = false });
    var execution = new FakeExecution();
    var resume = new QueueResumeOnStartupService(new EmptyProvider(), new FakeLifetime(), NullLogger<QueueResumeOnStartupService>.Instance);

    await resume.ResumeAsync(runState, queues, () => execution, CancellationToken.None).ConfigureAwait(true);

    execution.Started.Should().Equal("q1");
  }

  private static string ValueAfter(IReadOnlyList<string> args, string name) {
    var index = -1;
    for (var i = 0; i < args.Count; i++) {
      if (args[i] == name) {
        index = i;
        break;
      }
    }

    index.Should().BeGreaterThanOrEqualTo(0, name);
    return args[index + 1];
  }

  private sealed class EmptyProvider : IServiceProvider {
    public object? GetService(Type serviceType) => null;
  }

  private sealed class FakeQueues : IQueueRepository {
    public List<ExecutionQueue> Items { get; } = new();
    public Task<ExecutionQueue?> GetAsync(string id) => Task.FromResult(Items.FirstOrDefault(q => q.Id == id));
    public Task<IReadOnlyList<ExecutionQueue>> ListAsync() => Task.FromResult<IReadOnlyList<ExecutionQueue>>(Items);
    public Task<ExecutionQueue> CreateAsync(ExecutionQueue queue) => Task.FromResult(queue);
    public Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue) => Task.FromResult(queue);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private sealed class FakeExecution : IQueueExecutionService {
    public List<string> Started { get; } = new();

    public Task<QueueStartOutcome> StartAsync(string queueId, CancellationToken ct = default) {
      Started.Add(queueId);
      return Task.FromResult(QueueStartOutcome.Started);
    }

    public Task StopAsync(string queueId, CancellationToken ct = default) => Task.CompletedTask;
    public bool IsRunning(string queueId) => false;
    public LiveScheduleResult ScheduleRelative(string queueId, string sequenceId, TimeSpan offset) => new(LiveScheduleOutcome.NotRunning, default);
  }
}
