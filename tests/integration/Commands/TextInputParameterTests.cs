using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

#pragma warning disable CA2007, CA2000 // test code

namespace GameBot.IntegrationTests.Commands;

/// <summary>
/// Feature 192 (US2): a sequence gives the text of a TextInput step through a number parameter. The run
/// goes through the real DI graph: queue engine, sequence service, command executor, and resolver.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class TextInputParameterTests {
  public TextInputParameterTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  /// <summary>A session manager that keeps the input actions that it gets.</summary>
  private sealed class RecordingSessionManager : ISessionManager {
    private readonly List<EmulatorSession> _sessions = new();
    public List<InputAction> Actions { get; } = new();
    public int ActiveCount { get { lock (_sessions) return _sessions.Count; } }
    public bool CanCreateSession => true;

    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) {
      var session = new EmulatorSession {
        Id = Guid.NewGuid().ToString("N"), GameId = gameIdOrPath, DeviceSerial = preferredDeviceSerial, Status = SessionStatus.Running
      };
      lock (_sessions) _sessions.Add(session);
      return session;
    }

    public EmulatorSession? GetSession(string id) { lock (_sessions) return _sessions.FirstOrDefault(s => s.Id == id); }
    public IReadOnlyCollection<EmulatorSession> ListSessions() { lock (_sessions) return _sessions.ToList(); }
    public bool StopSession(string id) { lock (_sessions) return _sessions.RemoveAll(s => s.Id == id) > 0; }

    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) {
      var list = actions.ToList();
      lock (Actions) Actions.AddRange(list);
      return Task.FromResult(list.Count);
    }

    public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) =>
      Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));

    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }

  private static async Task SeedAsync(IServiceProvider services, string suffix, string? binding) {
    var command = new Command { Id = $"c-{suffix}", Name = $"Type-{suffix}" };
    command.Parameters.Add(new ParameterDeclaration { Name = "fighterBatch", Type = ParameterValueType.Number, Default = "250" });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.TextInput,
      Order = 0,
      TextInput = new TextInputConfig { Text = string.Empty },
      FieldTemplates = new Dictionary<string, string> { ["textInput.text"] = "{{fighterBatch}}" }
    });
    await services.GetRequiredService<ICommandRepository>().AddAsync(command);

    var step = new SequenceStep {
      Order = 0, StepId = "s1", StepType = SequenceStepType.Command, CommandId = command.Id,
      ParameterBindings = binding is null
        ? null
        : new Collection<ParameterBinding> { new() { Name = "fighterBatch", Value = binding } }
    };
    var sequence = new CommandSequence { Id = $"s-{suffix}", Name = $"Seq-{suffix}" };
    sequence.SetSteps(new[] { step });
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence);

    var template = new QueueTemplate { Id = $"tpl-{suffix}", Name = $"T-{suffix}" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = sequence.Id, ScheduleType = ScheduleType.OncePerRun });
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template);
    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = $"q-{suffix}", Name = $"Q-{suffix}", EmulatorSerial = "emu-offline", LinkedTemplateId = template.Id
    });
  }

  private static async Task RunAsync(IServiceProvider services, string queueId) {
    var engine = services.GetRequiredService<IQueueExecutionService>();
    await engine.StartAsync(queueId);
    var sw = Stopwatch.StartNew();
    while (engine.IsRunning(queueId) && sw.ElapsedMilliseconds < 10000) await Task.Delay(20);
  }

  private static (WebApplicationFactory<Program> App, RecordingSessionManager Sessions) Build() {
    var sessions = new RecordingSessionManager();
    var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(s => {
      s.RemoveAll<ISessionManager>();
      s.AddSingleton<ISessionManager>(sessions);
    }));
    _ = app.CreateClient();
    return (app, sessions);
  }

  [Fact]
  public async Task TheSequenceBindingGivesTheText() {
    var (app, sessions) = Build();
    using var _ = app;

    await SeedAsync(app.Services, "bound", "100");
    await RunAsync(app.Services, "q-bound");

    List<InputAction> textActions;
    lock (sessions.Actions) textActions = sessions.Actions.Where(a => a.Type == "text").ToList();
    textActions.Should().ContainSingle().Which.Args["text"].Should().Be("100");

    var page = await app.Services.GetRequiredService<IExecutionLogService>()
      .QueryAsync(new ExecutionLogQuery { ObjectType = "command", PageSize = 200 });
    var details = page.Items.SelectMany(i => i.Details ?? Array.Empty<ExecutionDetailItem>()).ToList();
    details.Where(d => d.Kind == "parameters").Select(d => d.Message).Should().Contain(m => m.Contains("fighterBatch=100", StringComparison.Ordinal));
    details.Should().Contain(d => d.Kind == "text-input" && d.Message == "Text input typed 3 characters.");
  }

  [Fact]
  public async Task WithNoBindingTheDefaultGivesTheText() {
    var (app, sessions) = Build();
    using var _ = app;

    await SeedAsync(app.Services, "default", null);
    await RunAsync(app.Services, "q-default");

    List<InputAction> textActions;
    lock (sessions.Actions) textActions = sessions.Actions.Where(a => a.Type == "text").ToList();
    textActions.Should().ContainSingle().Which.Args["text"].Should().Be("250");
  }
}
