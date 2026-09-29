using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
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

namespace GameBot.IntegrationTests.Commands;

/// <summary>
/// Feature 115 (issue #246) end-to-end: a <c>{{name}}</c> placeholder in a step <c>parameterBindings</c>
/// value resolves against the scope outside the binding. The tests do not upload images, so the tap
/// status can be different on different hosts. The tests assert what the log records about the
/// resolved values, not the tap status.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class BindingPlaceholderScopeIntegrationTests {
  private const string TapImageKey = "primitiveTap.detectionTarget.referenceImageId";
  private const string Name = "novaOptionImage";
  private const string Placeholder = "{{novaOptionImage}}";
  private const string EntryValue = "pns-alliance-nav-button";

  private const string UnresolvedHint =
      " Do one of these to supply a value for 'novaOptionImage'. Supply the value in the queue template entry or in the run request. "
      + "Give 'novaOptionImage' a default value in the sequence or in the calling command. "
      + "Bind a literal value, or bind a value in the calling command.";

  public BindingPlaceholderScopeIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  // ── Setup helpers ────────────────────────────────────────────────────────

  /// <summary>The command <c>ZZZ.NovaParamTap</c>: one tap step that gets its image id from <c>{{novaOptionImage}}</c>.</summary>
  private static Command NovaParamTap(string id) {
    var command = new Command { Id = id, Name = "ZZZ.NovaParamTap" };
    command.Parameters.Add(new ParameterDeclaration { Name = Name, Required = true });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("option-a", 0.85) },
      FieldTemplates = new Dictionary<string, string> { [TapImageKey] = Placeholder }
    });
    return command;
  }

  /// <summary>
  /// The command <c>ZZZ.Outer</c>: step 0 calls <paramref name="calleeId"/> with <paramref name="bindings"/>,
  /// and step 1 is a tap with a literal image id.
  /// </summary>
  private static Command Outer(string id, string calleeId, Collection<ParameterBinding>? bindings, bool declare) {
    var command = new Command { Id = id, Name = "ZZZ.Outer" };
    if (declare) command.Parameters.Add(new ParameterDeclaration { Name = Name, Required = true });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.Command,
      Order = 0,
      TargetId = calleeId,
      ParameterBindings = bindings
    });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 1,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("no-such-image-115", 0.85) }
    });
    return command;
  }

  private static Collection<ParameterBinding> Bind(string name, string value) =>
      new() { new ParameterBinding { Name = name, Value = value } };

  private static CommandSequence SequenceInvoking(
      string id,
      string commandId,
      Collection<ParameterBinding>? bindings,
      params ParameterDeclaration[] declarations) {
    var sequence = new CommandSequence { Id = id, Name = $"Seq-{id}" };
    foreach (var declaration in declarations) sequence.Parameters.Add(declaration);
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0, StepId = "s1", StepType = SequenceStepType.Command, CommandId = commandId, ParameterBindings = bindings
      }
    });
    return sequence;
  }

  private static ParameterDeclaration Declare(string name, bool required = true) => new() { Name = name, Required = required };

  private static async Task SeedAsync(IServiceProvider services, CommandSequence sequence, params Command[] commands) {
    var commandRepository = services.GetRequiredService<ICommandRepository>();
    foreach (var command in commands) await commandRepository.AddAsync(command).ConfigureAwait(false);
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
  }

  /// <summary>Makes a queue with one template entry for <paramref name="sequenceId"/> and runs it to the end.</summary>
  private static async Task RunEntryAsync(
      IServiceProvider services, string queueId, string sequenceId, params (string Name, string Value)[] values) {
    var entry = new QueueTemplateEntry { SequenceId = sequenceId, ScheduleType = ScheduleType.OncePerRun };
    foreach (var (name, value) in values) entry.ParameterValues.Add(new ParameterBinding { Name = name, Value = value });
    var template = new QueueTemplate { Id = $"tpl-{queueId}", Name = $"T-{queueId}" };
    template.Entries.Add(entry);
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template).ConfigureAwait(false);
    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = queueId, Name = $"Q-{queueId}", EmulatorSerial = "emu-offline", LinkedTemplateId = template.Id
    }).ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    await engine.StartAsync(queueId).ConfigureAwait(false);
    var sw = Stopwatch.StartNew();
    while (engine.IsRunning(queueId) && sw.ElapsedMilliseconds < 10000) {
      await Task.Delay(20).ConfigureAwait(false);
    }
  }

  /// <summary>The log entries of one object. The entry can come a short time after the queue stops, so the method waits.</summary>
  private static async Task<IReadOnlyList<ExecutionLogEntry>> EntriesAsync(
      IServiceProvider services, string objectType, string objectId, int expected = 1) {
    var log = services.GetRequiredService<IExecutionLogService>();
    var sw = Stopwatch.StartNew();
    while (true) {
      var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = objectType, PageSize = 200 })
          .ConfigureAwait(false);
      var entries = page.Items
          .Where(item => string.Equals(item.ObjectRef?.ObjectId, objectId, StringComparison.Ordinal))
          .ToList();
      if (entries.Count >= expected || sw.ElapsedMilliseconds > 10000) return entries;
      await Task.Delay(20).ConfigureAwait(false);
    }
  }

  private static async Task<IReadOnlyList<ExecutionDetailItem>> CommandDetailsAsync(IServiceProvider services, string commandId) =>
      (await EntriesAsync(services, "command", commandId).ConfigureAwait(false))
          .SelectMany(item => item.Details ?? Array.Empty<ExecutionDetailItem>())
          .ToList();

  private static string? AttributeText(ExecutionDetailItem item, string key) {
    if (item.Attributes is null || !item.Attributes.TryGetValue(key, out var value) || value is null) return null;
    return value switch {
      JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(),
      string text => text,
      IConvertible convertible => convertible.ToString(System.Globalization.CultureInfo.InvariantCulture),
      _ => JsonSerializer.Serialize(value)
    };
  }

  /// <summary>The <c>resolvedParameters</c> items of one <c>parameters</c> log item, as (name, value, originLayer).</summary>
  private static List<(string Name, string Value, string Layer)> Resolved(ExecutionDetailItem item) {
    var text = AttributeText(item, "resolvedParameters");
    text.Should().NotBeNull();
    using var document = JsonDocument.Parse(text!);
    return document.RootElement.EnumerateArray()
        .Select(p => (
            p.GetProperty("name").GetString()!,
            p.GetProperty("value").GetString()!,
            p.GetProperty("originLayer").GetString()!))
        .ToList();
  }

  private static ExecutionDetailItem ParametersItem(IEnumerable<ExecutionDetailItem> details) =>
      details.Should().ContainSingle(d => string.Equals(d.Kind, "parameters", StringComparison.Ordinal)).Subject;

  /// <summary>No log item records the placeholder text as a value.</summary>
  private static void ShouldNotContainPlaceholder(IEnumerable<ExecutionDetailItem> details) {
    foreach (var item in details) {
      item.Message.Should().NotContain(Placeholder);
      (AttributeText(item, "resolvedParameters") ?? string.Empty).Should().NotContain(Placeholder);
      (AttributeText(item, "referenceImageId") ?? string.Empty).Should().NotContain(Placeholder);
    }
  }

  private static HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    return client;
  }

  // ── US1: a binding placeholder gets the value of the sequence scope ────────

  [Fact]
  public async Task Issue246ReproductionGivesTheEntryValueWithTheEntryLayer() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services,
        SequenceInvoking("s115-repro", "c115-repro", Bind(Name, Placeholder), Declare(Name)),
        NovaParamTap("c115-repro")).ConfigureAwait(false);

    await RunEntryAsync(services, "q115-repro", "s115-repro", (Name, EntryValue)).ConfigureAwait(false);

    var details = await CommandDetailsAsync(services, "c115-repro").ConfigureAwait(false);
    Resolved(ParametersItem(details)).Should().Equal((Name, EntryValue, ParameterScopeLayers.Entry));
    ShouldNotContainPlaceholder(details);
  }

  [Fact]
  public async Task NestedCommandBindingResolvesAgainstTheCallingCommandScope() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services,
        SequenceInvoking("s115-nested", "c115-outer", null, Declare(Name)),
        NovaParamTap("c115-inner"),
        Outer("c115-outer", "c115-inner", Bind(Name, Placeholder), declare: true)).ConfigureAwait(false);

    await RunEntryAsync(services, "q115-nested", "s115-nested", (Name, EntryValue)).ConfigureAwait(false);

    // The nested command runs inside the calling command, so its steps are in the log of the calling command.
    var details = await CommandDetailsAsync(services, "c115-outer").ConfigureAwait(false);
    Resolved(ParametersItem(details)).Should().Equal((Name, EntryValue, ParameterScopeLayers.Entry));
    ShouldNotContainPlaceholder(details);
  }

  [Fact]
  public async Task MixedBindingValueRecordsTheCommandLayerAndTheSource() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services,
        SequenceInvoking("s115-mixed", "c115-mixed", Bind(Name, "nova-{{option}}"), Declare("option")),
        NovaParamTap("c115-mixed")).ConfigureAwait(false);

    await RunEntryAsync(services, "q115-mixed", "s115-mixed", ("option", "b")).ConfigureAwait(false);

    var details = await CommandDetailsAsync(services, "c115-mixed").ConfigureAwait(false);
    var item = ParametersItem(details);
    Resolved(item).Should().Equal(
        (Name, "nova-b", ParameterScopeLayers.Command),
        ("option", "b", ParameterScopeLayers.Entry));
    item.Message.Should().Be("Step 0 resolved 2 parameter(s): novaOptionImage=nova-b, option=b");
  }

  // ── US2: the current binding forms do not change ───────────────────────────

  [Fact]
  public async Task StepWithNoBindingsRecordsTheEntryValue() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services,
        SequenceInvoking("s115-none", "c115-none", null, Declare(Name)),
        NovaParamTap("c115-none")).ConfigureAwait(false);

    await RunEntryAsync(services, "q115-none", "s115-none", (Name, EntryValue)).ConfigureAwait(false);

    var details = await CommandDetailsAsync(services, "c115-none").ConfigureAwait(false);
    Resolved(ParametersItem(details)).Should().Equal((Name, EntryValue, ParameterScopeLayers.Entry));
  }

  [Fact]
  public async Task LiteralBindingRecordsTheCommandLayer() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services,
        SequenceInvoking("s115-literal", "c115-literal", Bind(Name, "pns-todo-radar"), Declare(Name)),
        NovaParamTap("c115-literal")).ConfigureAwait(false);

    await RunEntryAsync(services, "q115-literal", "s115-literal", (Name, EntryValue)).ConfigureAwait(false);

    var details = await CommandDetailsAsync(services, "c115-literal").ConfigureAwait(false);
    Resolved(ParametersItem(details)).Should().Equal((Name, "pns-todo-radar", ParameterScopeLayers.Command));
  }

  [Fact]
  public async Task SaveOfAnUndeclaredBindingPlaceholderIsRefused() {
    using var app = new WebApplicationFactory<Program>();
    var client = AuthedClient(app);
    await app.Services.GetRequiredService<ICommandRepository>().AddAsync(NovaParamTap("c115-save")).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = "ZZZ.UndeclaredBinding",
      steps = new object[] {
        new {
          stepId = "s1",
          stepType = "Action",
          primitiveAction = new { type = "command", schemaVersion = "v1", payload = new { commandId = "c115-save" } },
          parameterBindings = new object[] { new { name = Name, value = "{{undeclaredName}}" } }
        }
      }
    }).ConfigureAwait(false);

    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    JsonDocument.Parse(body).RootElement.GetProperty("error").GetString().Should().Be("unresolvable_parameter_reference");
  }

  [Fact]
  public async Task PlaceholderBindingAndLiteralCopyGiveTheSameFinalStatus() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services,
        SequenceInvoking("s115-ph", "c115-ph", Bind(Name, Placeholder), Declare(Name)),
        NovaParamTap("c115-ph")).ConfigureAwait(false);
    await services.GetRequiredService<ISequenceRepository>()
        .CreateAsync(SequenceInvoking("s115-lit", "c115-ph", Bind(Name, EntryValue), Declare(Name)))
        .ConfigureAwait(false);

    await RunEntryAsync(services, "q115-ph", "s115-ph", (Name, EntryValue)).ConfigureAwait(false);
    await RunEntryAsync(services, "q115-lit", "s115-lit", (Name, EntryValue)).ConfigureAwait(false);

    var placeholderRun = (await EntriesAsync(services, "sequence", "s115-ph").ConfigureAwait(false)).Should().ContainSingle().Subject;
    var literalRun = (await EntriesAsync(services, "sequence", "s115-lit").ConfigureAwait(false)).Should().ContainSingle().Subject;
    placeholderRun.FinalStatus.Should().Be(literalRun.FinalStatus);
  }

  // ── US3: an unresolved binding placeholder fails clearly ───────────────────

  [Fact]
  public async Task AdHocRunWithNoValueFailsTheStepWithTheHint() {
    using var app = new WebApplicationFactory<Program>();
    var client = AuthedClient(app);
    await SeedAsync(app.Services,
        SequenceInvoking("s115-adhoc", "c115-adhoc", Bind(Name, Placeholder), Declare(Name, required: false)),
        NovaParamTap("c115-adhoc")).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/sequences/s115-adhoc/execute", new { }).ConfigureAwait(false);

    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    var root = JsonDocument.Parse(body).RootElement;
    root.GetProperty("status").GetString().Should().Be("Failed");
    var step = root.GetProperty("steps").EnumerateArray().Should().ContainSingle().Subject;
    step.GetProperty("status").GetString().Should().Be("Failed");
    step.GetProperty("message").GetString().Should().Be(
        "Step 's1': parameter 'novaOptionImage' used by field 'parameterBindings.novaOptionImage' could not be resolved from any scope."
        + UnresolvedHint);

    // The command did not run, so no log item has the placeholder as a resolved value.
    var commandEntries = await EntriesAsync(app.Services, "command", "c115-adhoc", expected: 0).ConfigureAwait(false);
    ShouldNotContainPlaceholder(commandEntries.SelectMany(e => e.Details ?? Array.Empty<ExecutionDetailItem>()));
  }

  [Fact]
  public async Task NestedUnresolvedBindingIsSkippedAndTheNextStepRuns() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    // The save of a command does not check a nested binding value (spec Assumptions), so the calling
    // command can bind "{{missing}}" and not declare "missing".
    await SeedAsync(services,
        SequenceInvoking("s115-nmiss", "c115-nmiss-outer", null),
        NovaParamTap("c115-nmiss-inner"),
        Outer("c115-nmiss-outer", "c115-nmiss-inner", Bind(Name, "{{missing}}"), declare: false)).ConfigureAwait(false);

    await RunEntryAsync(services, "q115-nmiss", "s115-nmiss").ConfigureAwait(false);

    var details = await CommandDetailsAsync(services, "c115-nmiss-outer").ConfigureAwait(false);
    var skipped = details.Should().ContainSingle(d => string.Equals(d.Kind, "step", StringComparison.Ordinal)
        && d.Message.StartsWith("Step 0 was not executed", StringComparison.Ordinal)).Subject;
    AttributeText(skipped, "reasonCode").Should().Be("skipped_parameter_unresolved");
    AttributeText(skipped, "reason").Should().Be(
        "Step '0': parameter 'missing' used by field 'parameterBindings.novaOptionImage' could not be resolved from any scope. "
        + "Do one of these to supply a value for 'missing'. Supply the value in the queue template entry or in the run request. "
        + "Give 'missing' a default value in the sequence or in the calling command. "
        + "Bind a literal value, or bind a value in the calling command.");

    // The nested command did not run: no parameters item and no tap.
    details.Should().NotContain(d => string.Equals(d.Kind, "parameters", StringComparison.Ordinal));
    details.Should().NotContain(d => string.Equals(d.Kind, "tap", StringComparison.Ordinal));

    // The next step of the calling command ran (its result depends on the host, so only its item is asserted).
    details.Should().Contain(d => d.Message.StartsWith("Step 1", StringComparison.Ordinal));
  }
}
