using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Games;
using GameBot.Domain.Queues;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.EnsureGameRunning;
using Xunit;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Services.EnsureGameRunning;

public sealed class EnsureGameRunningActionHandlerTests {
  // ── Fakes ─────────────────────────────────────────────────────────────────

  private sealed class FakeSessionManager : ISessionManager {
    private readonly Dictionary<string, EmulatorSession> _sessions = new(StringComparer.Ordinal);
    public int ActiveCount => _sessions.Count;
    public bool CanCreateSession => true;
    public void Seed(EmulatorSession s) => _sessions[s.Id] = s;
    public EmulatorSession? GetSession(string id) => _sessions.TryGetValue(id, out var s) ? s : null;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();
    public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions.Values.ToList();
    public bool StopSession(string id) => _sessions.Remove(id);
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  private sealed class FakeQueueRepository : IQueueRepository {
    private readonly Dictionary<string, ExecutionQueue> _items = new(StringComparer.Ordinal);
    public void Seed(ExecutionQueue q) => _items[q.Id] = q;
    public Task<ExecutionQueue?> GetAsync(string id) => Task.FromResult(_items.TryGetValue(id, out var q) ? q : null);
    public Task<IReadOnlyList<ExecutionQueue>> ListAsync() => Task.FromResult((IReadOnlyList<ExecutionQueue>)_items.Values.ToList());
    public Task<ExecutionQueue> CreateAsync(ExecutionQueue q) { _items[q.Id] = q; return Task.FromResult(q); }
    public Task<ExecutionQueue> UpdateAsync(ExecutionQueue q) { _items[q.Id] = q; return Task.FromResult(q); }
    public Task<bool> DeleteAsync(string id) => Task.FromResult(_items.Remove(id));
  }

  private sealed class FakeGameRepository : IGameRepository {
    private readonly Dictionary<string, GameArtifact> _items = new(StringComparer.Ordinal);
    public void Seed(GameArtifact g) => _items[g.Id] = g;
    public Task<GameArtifact> AddAsync(GameArtifact g, CancellationToken ct = default) { _items[g.Id] = g; return Task.FromResult(g); }
    public Task<GameArtifact?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult(_items.TryGetValue(id, out var g) ? g : null);
    public Task<IReadOnlyList<GameArtifact>> ListAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<GameArtifact>)_items.Values.ToList());
    public Task<GameArtifact?> UpdateAsync(GameArtifact g, CancellationToken ct = default) { _items[g.Id] = g; return Task.FromResult<GameArtifact?>(g); }
    public Task<bool> DeleteAsync(string id, CancellationToken ct = default) => Task.FromResult(_items.Remove(id));
  }

  private sealed class FakeAdbGameOperations : IAdbGameOperations {
    public string? ForegroundPackage { get; set; }
    public List<string> LaunchedPackages { get; } = new();
    public Task<string?> GetForegroundPackageAsync(string deviceSerial, CancellationToken ct = default) => Task.FromResult(ForegroundPackage);
    public Task LaunchAppAsync(string deviceSerial, string packageName, CancellationToken ct = default) { LaunchedPackages.Add(packageName); return Task.CompletedTask; }
  }

  // ── Helpers ───────────────────────────────────────────────────────────────

  private static EmulatorSession QueueSession(string sessionId, string queueId, string? serial = "emulator-5554") =>
    new() { Id = sessionId, GameId = $"queue:{queueId}", Status = SessionStatus.Running, DeviceSerial = serial };

  private static EnsureGameRunningActionHandler BuildHandler(
    FakeSessionManager sessions, FakeQueueRepository queues, FakeGameRepository games, FakeAdbGameOperations adb) =>
    new(sessions, queues, games, adb);

  // ── Tests ─────────────────────────────────────────────────────────────────

  [Fact]
  public async Task ReturnsNoQueueContextWhenSessionNotFound() {
    var handler = BuildHandler(new(), new(), new(), new());
    var result = await handler.ExecuteAsync("missing-session");
    result.Outcome.Should().Be(EnsureGameRunningOutcome.NoQueueContext);
    result.IsSuccess.Should().BeFalse();
  }

  [Fact]
  public async Task ReturnsNoQueueContextWhenSessionLabelHasNoQueuePrefix() {
    var sessions = new FakeSessionManager();
    sessions.Seed(new EmulatorSession { Id = "s1", GameId = "direct-session", Status = SessionStatus.Running });
    var handler = BuildHandler(sessions, new(), new(), new());
    var result = await handler.ExecuteAsync("s1");
    result.Outcome.Should().Be(EnsureGameRunningOutcome.NoQueueContext);
  }

  [Fact]
  public async Task ReturnsNoLinkedGameWhenQueueHasNoLinkedGameId() {
    var sessions = new FakeSessionManager();
    sessions.Seed(QueueSession("s1", "q1"));
    var queues = new FakeQueueRepository();
    queues.Seed(new ExecutionQueue { Id = "q1", Name = "Q", EmulatorSerial = "x" });
    var handler = BuildHandler(sessions, queues, new(), new());
    var result = await handler.ExecuteAsync("s1");
    result.Outcome.Should().Be(EnsureGameRunningOutcome.NoLinkedGame);
  }

  [Fact]
  public async Task ReturnsNoPackageNameWhenGameHasNoPackageName() {
    var sessions = new FakeSessionManager();
    sessions.Seed(QueueSession("s1", "q1"));
    var queues = new FakeQueueRepository();
    queues.Seed(new ExecutionQueue { Id = "q1", Name = "Q", EmulatorSerial = "x", LinkedGameId = "g1" });
    var games = new FakeGameRepository();
    games.Seed(new GameArtifact { Id = "g1", Name = "MyGame" });
    var handler = BuildHandler(sessions, queues, games, new());
    var result = await handler.ExecuteAsync("s1");
    result.Outcome.Should().Be(EnsureGameRunningOutcome.NoPackageName);
  }

  [Fact]
  public async Task ReturnsGameRunningWhenForegroundPackageMatches() {
    var sessions = new FakeSessionManager();
    sessions.Seed(QueueSession("s1", "q1"));
    var queues = new FakeQueueRepository();
    queues.Seed(new ExecutionQueue { Id = "q1", Name = "Q", EmulatorSerial = "x", LinkedGameId = "g1" });
    var games = new FakeGameRepository();
    games.Seed(new GameArtifact { Id = "g1", Name = "MyGame", PackageName = "com.example.game" });
    var adb = new FakeAdbGameOperations { ForegroundPackage = "com.example.game" };
    var handler = BuildHandler(sessions, queues, games, adb);

    var result = await handler.ExecuteAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.GameRunning);
    result.IsSuccess.Should().BeTrue();
    result.ReasonCode.Should().Be("game_running");
    adb.LaunchedPackages.Should().BeEmpty();
  }

  [Fact]
  public async Task ReturnsGameNotRunningAndLaunchesAppWhenForegroundPackageDoesNotMatch() {
    var sessions = new FakeSessionManager();
    sessions.Seed(QueueSession("s1", "q1"));
    var queues = new FakeQueueRepository();
    queues.Seed(new ExecutionQueue { Id = "q1", Name = "Q", EmulatorSerial = "x", LinkedGameId = "g1" });
    var games = new FakeGameRepository();
    games.Seed(new GameArtifact { Id = "g1", Name = "MyGame", PackageName = "com.example.game" });
    var adb = new FakeAdbGameOperations { ForegroundPackage = "com.other.app" };
    var handler = BuildHandler(sessions, queues, games, adb);

    var result = await handler.ExecuteAsync("s1");

    result.Outcome.Should().Be(EnsureGameRunningOutcome.GameNotRunning);
    result.IsSuccess.Should().BeFalse();
    result.ReasonCode.Should().Be("game_not_running");
    adb.LaunchedPackages.Should().ContainSingle("com.example.game");
  }
}
