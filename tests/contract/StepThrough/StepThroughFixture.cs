#pragma warning disable CA2007, CA1861, CA1859, CA1849, CA1054, CA1056, CA1307, CA1308
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameBot.ContractTests.StepThrough;

/// <summary>A session manager that records the input and can hold an input in flight.</summary>
internal sealed class RecordingSessions : ISessionManager {
  private readonly List<EmulatorSession> _sessions = new();

  public List<InputAction> Inputs { get; } = new();

  /// <summary>When set, an input waits for this task. A cancelled token ends the wait.</summary>
  public TaskCompletionSource<bool>? Hold { get; set; }

  public int ActiveCount => _sessions.Count;

  public bool CanCreateSession => false;

  public EmulatorSession Add(string id, string serial, SessionStatus status = SessionStatus.Running) {
    var session = new EmulatorSession { Id = id, GameId = "queue:q1", Status = status, DeviceSerial = serial };
    lock (_sessions) _sessions.Add(session);
    return session;
  }

  public EmulatorSession? GetSession(string id) {
    lock (_sessions) return _sessions.FirstOrDefault(s => s.Id == id);
  }

  public EmulatorSession CreateSession(string g, string? s = null) => throw new NotSupportedException();

  public IReadOnlyCollection<EmulatorSession> ListSessions() {
    lock (_sessions) return _sessions.ToArray();
  }

  public bool StopSession(string id) => false;

  public async Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) {
    var hold = Hold;
    if (hold is not null) await hold.Task.WaitAsync(ct);
    lock (Inputs) Inputs.AddRange(actions);
    return 1;
  }

  public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default)
    => Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));

  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}

/// <summary>
/// One service host for the step-through contract tests. It has its own data folder and a recording
/// session manager. Each test uses its own game session id, so tests do not meet in the one-step-through
/// rule for a game session.
/// </summary>
public sealed class StepThroughFixture : IDisposable {
  private readonly string? _prevAuthToken;
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynamicPort;
  private readonly string? _prevDataDir;
  private readonly string _dataDir;
  private readonly WebApplicationFactory<Program> _baseFactory;

  public StepThroughFixture() {
    _prevAuthToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynamicPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevDataDir = Environment.GetEnvironmentVariable("GAMEBOT_DATA_DIR");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    _dataDir = Path.Combine(Path.GetTempPath(), "GameBotContractTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_dataDir);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _dataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", _dataDir);

    _baseFactory = new WebApplicationFactory<Program>();
    App = _baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => {
      services.RemoveAll<ISessionManager>();
      services.AddSingleton<ISessionManager>(Sessions);
    }));
    Client = App.CreateClient();
    Client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
  }

  internal RecordingSessions Sessions { get; } = new();

  internal WebApplicationFactory<Program> App { get; }

  public HttpClient Client { get; }

  public IServiceProvider Services => App.Services;

  /// <summary>Adds a connected game session and returns its id.</summary>
  internal string NewGameSession(string? serial = null) {
    var id = "gs-" + Guid.NewGuid().ToString("N")[..8];
    Sessions.Add(id, serial ?? "emu-" + Guid.NewGuid().ToString("N")[..8]);
    return id;
  }

  public static SequenceStep Tap(int order, string id, int x = 10, int y = 20) {
    var step = new SequenceStep {
      Order = order,
      StepId = id,
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = "tap" }
    };
    step.Action.Parameters["x"] = x;
    step.Action.Parameters["y"] = y;
    return step;
  }

  public async Task<string> SeedSequenceAsync(string name, params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq-" + Guid.NewGuid().ToString("N")[..10], Name = name };
    sequence.SetSteps(steps);
    var repository = Services.GetRequiredService<ISequenceRepository>();
    await repository.CreateAsync(sequence);
    return sequence.Id;
  }

  public async Task<string> SeedParameterizedSequenceAsync(string name, GameBot.Domain.Parameters.ParameterDeclaration[] parameters, params SequenceStep[] steps) {
    ArgumentNullException.ThrowIfNull(parameters);
    var sequence = new CommandSequence { Id = "seq-" + Guid.NewGuid().ToString("N")[..10], Name = name };
    sequence.SetSteps(steps);
    foreach (var parameter in parameters) sequence.Parameters.Add(parameter);
    await Services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence);
    return sequence.Id;
  }

  /// <summary>Registers a queue run that owns <paramref name="serial"/>. Dispose the result to remove it.</summary>
  internal QueueAttachment AttachQueue(string serial, string name = "Daily") {
    var queueId = "q-" + Guid.NewGuid().ToString("N")[..6];
    var runs = Services.GetRequiredService<GameBot.Service.Services.QueueExecution.IQueueRunRegistry>();
    var claims = Services.GetRequiredService<GameBot.Service.Services.QueueExecution.IDeviceClaimRegistry>();
    var handle = new GameBot.Service.Services.QueueExecution.QueueRunHandle { QueueId = queueId, Cts = new CancellationTokenSource() };
    if (!runs.TryAdd(queueId, handle) || !claims.TryClaim(serial, queueId, name)) {
      throw new InvalidOperationException("The queue could not be attached.");
    }

    return new QueueAttachment(queueId, name, serial, handle, runs, claims);
  }

  public async Task ReplaceSequenceAsync(string id, params SequenceStep[] steps) {
    var repository = Services.GetRequiredService<ISequenceRepository>();
    var sequence = (await repository.GetAsync(id))!;
    sequence.SetSteps(steps);
    await repository.UpdateAsync(sequence);
  }

  public async Task<(int Status, JsonElement Body)> SendAsync(HttpMethod method, string url, object? body = null) {
    using var request = new HttpRequestMessage(method, new Uri(url, UriKind.Relative));
    if (body is not null) request.Content = JsonContent.Create(body);
    using var response = await Client.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    var element = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    return ((int)response.StatusCode, element);
  }

  public Task<(int Status, JsonElement Body)> PostAsync(string url, object? body = null) => SendAsync(HttpMethod.Post, url, body);

  public Task<(int Status, JsonElement Body)> GetAsync(string url) => SendAsync(HttpMethod.Get, url);

  public async Task<string> StartAsync(string sequenceId, string gameSessionId, object? extra = null) {
    var (status, body) = await PostAsync("/api/step-through", extra is null
      ? new { sequenceId, gameSessionId }
      : MergeStart(sequenceId, gameSessionId, extra));
    if (status != 201) throw new InvalidOperationException($"Start failed with {status}: {body}");
    return body.GetProperty("id").GetString()!;
  }

  private static Dictionary<string, object?> MergeStart(string sequenceId, string gameSessionId, object extra) {
    var map = new Dictionary<string, object?> { ["sequenceId"] = sequenceId, ["gameSessionId"] = gameSessionId };
    foreach (var property in extra.GetType().GetProperties()) map[property.Name] = property.GetValue(extra);
    return map;
  }

  /// <summary>Waits until no step runs, and returns the state.</summary>
  public async Task<JsonElement> WaitIdleAsync(string id, int timeoutMs = 10000) {
    var sw = System.Diagnostics.Stopwatch.StartNew();
    while (true) {
      var (status, body) = await GetAsync($"/api/step-through/{id}");
      if (status != 200) throw new InvalidOperationException($"Read failed with {status}: {body}");
      if (body.GetProperty("state").GetString() != "running") return body;
      if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("The step did not end: " + body);
      await Task.Delay(25);
    }
  }

  public void Dispose() {
    Client.Dispose();
    App.Dispose();
    _baseFactory.Dispose();
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevAuthToken);
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynamicPort);
    Environment.SetEnvironmentVariable("GAMEBOT_DATA_DIR", _prevDataDir);
    Environment.SetEnvironmentVariable("Service__Storage__Root", null);
    try { Directory.Delete(_dataDir, recursive: true); }
    catch (IOException) { /* best effort */ }
    catch (UnauthorizedAccessException) { /* best effort */ }
  }
}

/// <summary>A queue run that a test attached to a device. Dispose it to remove the run and the claim.</summary>
internal sealed class QueueAttachment : IDisposable {
  private readonly GameBot.Service.Services.QueueExecution.IQueueRunRegistry _runs;
  private readonly GameBot.Service.Services.QueueExecution.IDeviceClaimRegistry _claims;

  public QueueAttachment(
      string queueId,
      string queueName,
      string serial,
      GameBot.Service.Services.QueueExecution.QueueRunHandle handle,
      GameBot.Service.Services.QueueExecution.IQueueRunRegistry runs,
      GameBot.Service.Services.QueueExecution.IDeviceClaimRegistry claims) {
    QueueId = queueId;
    QueueName = queueName;
    Serial = serial;
    Handle = handle;
    _runs = runs;
    _claims = claims;
  }

  public string QueueId { get; }

  public string QueueName { get; }

  public string Serial { get; }

  public GameBot.Service.Services.QueueExecution.QueueRunHandle Handle { get; }

  public void Dispose() {
    _runs.Remove(QueueId, out _);
    _claims.Release(Serial, QueueId);
  }
}
