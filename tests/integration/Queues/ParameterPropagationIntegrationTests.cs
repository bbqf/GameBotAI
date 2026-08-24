using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Parameters;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 078 end-to-end: a value supplied on a queue-template entry (or by the queue itself)
/// reaching a command's step through the real DI graph — queue engine → SequenceExecutionService →
/// CommandExecutor → CommandStepResolver — plus the pre-run refusal that keeps a misconfigured queue
/// from starting at all.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class ParameterPropagationIntegrationTests {
  public ParameterPropagationIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  /// <summary>
  /// A command whose key-input step reads its key from <paramref name="reference"/>. Key input is
  /// infrastructure-free with ADB stubbed, so the run exercises resolution without touching a device.
  /// </summary>
  private static Command KeyCommand(string id, string reference, params ParameterDeclaration[] declarations) {
    var command = new Command { Id = id, Name = $"Cmd-{id}" };
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.KeyInput,
      Order = 0,
      KeyInput = new KeyInputConfig { Key = reference }
    });
    foreach (var declaration in declarations) command.Parameters.Add(declaration);
    return command;
  }

  private static CommandSequence SequenceInvoking(string id, string commandId) {
    var sequence = new CommandSequence { Id = id, Name = $"Seq-{id}" };
    sequence.SetSteps(new[] {
      new SequenceStep { Order = 0, StepId = "s1", StepType = SequenceStepType.Command, CommandId = commandId }
    });
    return sequence;
  }

  private static async Task SeedAsync(
      IServiceProvider services,
      Command command,
      CommandSequence sequence,
      string queueId,
      Action<QueueTemplateEntry>? configureEntry = null,
      Action<ExecutionQueue>? configureQueue = null) {
    await services.GetRequiredService<ICommandRepository>().AddAsync(command).ConfigureAwait(false);
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);

    var entry = new QueueTemplateEntry { SequenceId = sequence.Id, ScheduleType = ScheduleType.OncePerRun };
    configureEntry?.Invoke(entry);
    var template = new QueueTemplate { Id = $"tpl-{queueId}", Name = $"T-{queueId}" };
    template.Entries.Add(entry);
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template).ConfigureAwait(false);

    var queue = new ExecutionQueue {
      Id = queueId, Name = $"Q-{queueId}", EmulatorSerial = "emu-offline", LinkedTemplateId = template.Id
    };
    configureQueue?.Invoke(queue);
    await services.GetRequiredService<IQueueRepository>().CreateAsync(queue).ConfigureAwait(false);
  }

  private static async Task RunToCompletionAsync(IQueueExecutionService engine, string queueId) {
    await engine.StartAsync(queueId).ConfigureAwait(false);
    var sw = Stopwatch.StartNew();
    while (engine.IsRunning(queueId) && sw.ElapsedMilliseconds < 10000) {
      await Task.Delay(20).ConfigureAwait(false);
    }
  }

  /// <summary>The resolved-parameter detail items recorded for every command in the run.</summary>
  private static async Task<string> ResolvedParameterTextAsync(IExecutionLogService log) {
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "command", PageSize = 200 })
        .ConfigureAwait(false);
    var details = page.Items
        .SelectMany(item => item.Details ?? Array.Empty<ExecutionDetailItem>())
        .Where(d => string.Equals(d.Kind, "parameters", StringComparison.Ordinal))
        .Select(d => d.Message);
    return string.Join(" | ", details);
  }

  // ── FR-012 / FR-012a: values reach the command that consumes them ─────────

  [Fact]
  public async Task TemplateEntryValueReachesTheCommandThatDeclaresIt() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    await SeedAsync(
        services,
        KeyCommand("c-entry", "{{keyName}}", new ParameterDeclaration { Name = "keyName" }),
        SequenceInvoking("s-entry", "c-entry"),
        "q-entry",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "keyName", Value = "KEYCODE_HOME" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-entry").ConfigureAwait(false);

    var text = await ResolvedParameterTextAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("keyName=KEYCODE_HOME");
  }

  [Fact]
  public async Task AdHocEntryValueReachesACommandTheSequenceNeverDeclares() {
    // FR-012a: the sequence declares nothing at all; the entry supplies the name and the leaf command
    // declares and consumes it. This is the zero-ceremony pass-through the feature exists to enable.
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    await SeedAsync(
        services,
        KeyCommand("c-adhoc", "{{adbKey}}", new ParameterDeclaration { Name = "adbKey" }),
        SequenceInvoking("s-adhoc", "c-adhoc"),
        "q-adhoc",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "adbKey", Value = "KEYCODE_BACK" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-adhoc").ConfigureAwait(false);

    var text = await ResolvedParameterTextAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("adbKey=KEYCODE_BACK");
  }

  [Fact]
  public async Task DeclaredDefaultAppliesWhenNothingSuppliesTheValue() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    await SeedAsync(
        services,
        KeyCommand("c-default", "{{keyName}}", new ParameterDeclaration { Name = "keyName", Default = "KEYCODE_ENTER" }),
        SequenceInvoking("s-default", "c-default"),
        "q-default")
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-default").ConfigureAwait(false);

    var text = await ResolvedParameterTextAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("keyName=KEYCODE_ENTER");
  }

  [Fact]
  public async Task EntryValueOverridesTheDeclaredDefault() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    await SeedAsync(
        services,
        KeyCommand("c-over", "{{keyName}}", new ParameterDeclaration { Name = "keyName", Default = "KEYCODE_ENTER" }),
        SequenceInvoking("s-over", "c-over"),
        "q-over",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "keyName", Value = "KEYCODE_HOME" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-over").ConfigureAwait(false);

    var text = await ResolvedParameterTextAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("keyName=KEYCODE_HOME");
    text.Should().NotContain("KEYCODE_ENTER");
  }

  [Fact]
  public async Task QueueBuiltInReachesTheCommandWithNoEntryConfiguration() {
    // FR-010 / SC-002: the queue already stores its serial, so this needs no parameter setup at all.
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    await SeedAsync(
        services,
        KeyCommand("c-builtin", "{{queue.emulatorSerial}}"),
        SequenceInvoking("s-builtin", "c-builtin"),
        "q-builtin",
        configureQueue: queue => queue.EmulatorSerial = "emulator-5560")
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-builtin").ConfigureAwait(false);

    var text = await ResolvedParameterTextAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("queue.emulatorSerial=emulator-5560");
  }

  // ── FR-017 / FR-018: an unresolved name fails the step and dispatches nothing ──

  [Fact]
  public async Task UnresolvableParameterFailsTheStepWithoutDispatching() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    await SeedAsync(
        services,
        KeyCommand("c-missing", "{{nobodySuppliesThis}}", new ParameterDeclaration { Name = "nobodySuppliesThis" }),
        SequenceInvoking("s-missing", "c-missing"),
        "q-missing")
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-missing").ConfigureAwait(false);

    var log = services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "command", PageSize = 200 }).ConfigureAwait(false);
    var messages = string.Join(" | ", page.Items
        .SelectMany(item => item.Details ?? Array.Empty<ExecutionDetailItem>())
        .Select(d => d.Message));

    messages.Should().Contain("could not be resolved from any scope");
    messages.Should().Contain("nobodySuppliesThis");
    // Nothing resolved, so no resolved-parameter record was written for the step.
    messages.Should().NotContain("nobodySuppliesThis=");
  }

  // ── FR-022: a queue that cannot satisfy a required parameter is refused ───

  [Fact]
  public async Task StartingAQueueIsRefusedWhenARequiredParameterCannotBeSupplied() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
    var services = app.Services;

    await SeedAsync(
        services,
        KeyCommand("c-req", "{{mustBeSupplied}}",
            new ParameterDeclaration { Name = "mustBeSupplied", Required = true }),
        SequenceInvoking("s-req", "c-req"),
        "q-req")
      .ConfigureAwait(false);

    var response = await client.PostAsync(new Uri("/api/queues/q-req/start", UriKind.Relative), content: null).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    body.Should().Contain("missing_required_parameters");
    body.Should().Contain("mustBeSupplied");
    services.GetRequiredService<IQueueExecutionService>().IsRunning("q-req").Should().BeFalse();
  }

  [Fact]
  public async Task StartingAQueueSucceedsOnceTheRequiredParameterIsSupplied() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
    var services = app.Services;

    await SeedAsync(
        services,
        KeyCommand("c-ok", "{{mustBeSupplied}}",
            new ParameterDeclaration { Name = "mustBeSupplied", Required = true }),
        SequenceInvoking("s-ok", "c-ok"),
        "q-ok",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "mustBeSupplied", Value = "KEYCODE_HOME" }))
      .ConfigureAwait(false);

    var response = await client.PostAsync(new Uri("/api/queues/q-ok/start", UriKind.Relative), content: null).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task AnUnparametrizedQueueStartsExactlyAsBefore() {
    // FR-032 / SC-007: the pre-flight check must be invisible to everything that predates the feature.
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
    var services = app.Services;

    await SeedAsync(
        services,
        KeyCommand("c-plain", "KEYCODE_HOME"),
        SequenceInvoking("s-plain", "c-plain"),
        "q-plain")
      .ConfigureAwait(false);

    var response = await client.PostAsync(new Uri("/api/queues/q-plain/start", UriKind.Relative), content: null).ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }
}
