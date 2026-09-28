using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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
/// Feature 114 end-to-end: a template entry value selects the reference image of a tap step and of a
/// wait step. The tests do not upload images for the resolved ids, so the step status can be
/// different on different hosts. The tests assert only what the log records about the resolved value.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class ParametrizedReferenceImageIntegrationTests {
  private const string TapImageKey = "primitiveTap.detectionTarget.referenceImageId";
  private const string WaitImageKey = "waitForImage.detectionTarget.referenceImageId";
  private static readonly string[] RunValues = { "option-b", "option-c" };
  private static readonly int[] StepOrders = { 0, 1 };

  public ParametrizedReferenceImageIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  /// <summary>A tap step and a wait step. Both get their image id from the overlay value <c>{{novaOption}}</c>.</summary>
  private static Command OverlayCommand(string id, bool required = true) {
    var command = new Command { Id = id, Name = $"Cmd-{id}" };
    command.Parameters.Add(new ParameterDeclaration { Name = "novaOption", Required = required });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("option-a", 0.85) },
      FieldTemplates = new Dictionary<string, string> { [TapImageKey] = "{{novaOption}}" }
    });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.WaitForImage,
      Order = 1,
      WaitForImage = new WaitForImageConfig { DetectionTarget = new DetectionTarget("option-a"), TimeoutMs = 20 },
      FieldTemplates = new Dictionary<string, string> { [WaitImageKey] = "{{novaOption}}" }
    });
    return command;
  }

  /// <summary>The same two steps with the stored inline form and no overlay.</summary>
  private static Command InlineCommand(string id) {
    var command = new Command { Id = id, Name = $"Cmd-{id}" };
    command.Parameters.Add(new ParameterDeclaration { Name = "novaOption", Required = true });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("{{novaOption}}", 0.85) }
    });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.WaitForImage,
      Order = 1,
      WaitForImage = new WaitForImageConfig { DetectionTarget = new DetectionTarget("{{novaOption}}"), TimeoutMs = 20 }
    });
    return command;
  }

  private static CommandSequence SequenceInvoking(
      string id,
      string commandId,
      Collection<ParameterBinding>? bindings = null) {
    var sequence = new CommandSequence { Id = id, Name = $"Seq-{id}" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0, StepId = "s1", StepType = SequenceStepType.Command, CommandId = commandId, ParameterBindings = bindings
      }
    });
    return sequence;
  }

  private static async Task SeedAsync(IServiceProvider services, Command command, CommandSequence sequence) {
    await services.GetRequiredService<ICommandRepository>().AddAsync(command).ConfigureAwait(false);
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
  }

  /// <summary>Makes a queue with one template entry for <paramref name="sequenceId"/> and runs it to the end.</summary>
  private static async Task RunEntryAsync(IServiceProvider services, string queueId, string sequenceId, string? value) {
    var entry = new QueueTemplateEntry { SequenceId = sequenceId, ScheduleType = ScheduleType.OncePerRun };
    if (value is not null) entry.ParameterValues.Add(new ParameterBinding { Name = "novaOption", Value = value });
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

  /// <summary>
  /// All command log detail items of <paramref name="commandId"/>. The log entry can come a short
  /// time after the queue stops, so the method waits for <paramref name="expectedRuns"/> entries.
  /// </summary>
  private static async Task<IReadOnlyList<ExecutionDetailItem>> CommandDetailsAsync(
      IServiceProvider services, string commandId, int expectedRuns = 1) {
    var log = services.GetRequiredService<IExecutionLogService>();
    var sw = Stopwatch.StartNew();
    while (true) {
      var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "command", PageSize = 200 })
          .ConfigureAwait(false);
      var entries = page.Items
          .Where(item => string.Equals(item.ObjectRef?.ObjectId, commandId, StringComparison.Ordinal))
          .ToList();
      if (entries.Count >= expectedRuns || sw.ElapsedMilliseconds > 10000) {
        return entries.SelectMany(item => item.Details ?? Array.Empty<ExecutionDetailItem>()).ToList();
      }

      await Task.Delay(20).ConfigureAwait(false);
    }
  }

  private static string? AttributeText(ExecutionDetailItem item, string key) {
    if (item.Attributes is null || !item.Attributes.TryGetValue(key, out var value) || value is null) return null;
    return value switch {
      JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(),
      string text => text,
      IConvertible convertible => convertible.ToString(System.Globalization.CultureInfo.InvariantCulture),
      _ => JsonSerializer.Serialize(value)
    };
  }

  /// <summary>The <c>parameters</c> items of one step order.</summary>
  private static List<ExecutionDetailItem> ParameterItems(IEnumerable<ExecutionDetailItem> details, int stepOrder) =>
      details.Where(d => string.Equals(d.Kind, "parameters", StringComparison.Ordinal)
          && AttributeText(d, "stepOrder") == stepOrder.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .ToList();

  /// <summary>The <c>step</c> items of the wait step.</summary>
  private static List<ExecutionDetailItem> WaitStepItems(IEnumerable<ExecutionDetailItem> details) =>
      details.Where(d => string.Equals(d.Kind, "step", StringComparison.Ordinal)
          && string.Equals(AttributeText(d, "stepType"), "waitForImage", StringComparison.OrdinalIgnoreCase))
        .ToList();

  private static void ShouldRecordValue(ExecutionDetailItem item, string value, string layer = ParameterScopeLayers.Entry) {
    item.Message.Should().Contain($"novaOption={value}");
    var text = AttributeText(item, "resolvedParameters");
    text.Should().Contain("novaOption").And.Contain(value).And.Contain($"\"{layer}\"");
  }

  // ── US1: the value selects the image (FR-003, FR-011) ─────────────────────

  [Fact]
  public async Task EachRunUsesTheImageThatTheEntryValueSelects() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, OverlayCommand("c-img"), SequenceInvoking("s-img", "c-img")).ConfigureAwait(false);

    foreach (var value in RunValues) {
      await RunEntryAsync(services, $"q-img-{value}", "s-img", value).ConfigureAwait(false);
    }

    var details = await CommandDetailsAsync(services, "c-img", expectedRuns: RunValues.Length).ConfigureAwait(false);
    var waitItems = WaitStepItems(details);
    waitItems.Select(d => AttributeText(d, "referenceImageId"))
        .Should().BeEquivalentTo(RunValues);

    foreach (var order in StepOrders) {
      var items = ParameterItems(details, order);
      items.Should().HaveCount(2);
      ShouldRecordValue(items.Single(d => d.Message.Contains("option-b", StringComparison.Ordinal)), "option-b");
      ShouldRecordValue(items.Single(d => d.Message.Contains("option-c", StringComparison.Ordinal)), "option-c");
    }
  }

  [Fact]
  public async Task InlinePlaceholderWithNoOverlayResolvesAsBefore() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, InlineCommand("c-inline"), SequenceInvoking("s-inline", "c-inline")).ConfigureAwait(false);

    await RunEntryAsync(services, "q-inline", "s-inline", "option-b").ConfigureAwait(false);

    var details = await CommandDetailsAsync(services, "c-inline").ConfigureAwait(false);
    WaitStepItems(details).Should().ContainSingle()
        .Which.Should().Match<ExecutionDetailItem>(d => AttributeText(d, "referenceImageId") == "option-b");
    ShouldRecordValue(ParameterItems(details, 0).Single(), "option-b");
    ShouldRecordValue(ParameterItems(details, 1).Single(), "option-b");
  }

  // ── US3: run-time failures (FR-008, FR-009, SC-004) ──────────────────────

  /// <summary>The "not executed" item of the tap step (step order 0).</summary>
  private static ExecutionDetailItem TapNotExecutedItem(IEnumerable<ExecutionDetailItem> details) =>
      details.Single(d => string.Equals(d.Kind, "step", StringComparison.Ordinal)
          && d.Message.StartsWith("Step 0 was not executed", StringComparison.Ordinal));

  [Fact]
  public async Task ResolvedIdWithNoImageGivesTheMissingImageResultOfEachStepType() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, OverlayCommand("c-missing"), SequenceInvoking("s-missing", "c-missing")).ConfigureAwait(false);

    await RunEntryAsync(services, "q-missing", "s-missing", "no-such-image").ConfigureAwait(false);

    var details = await CommandDetailsAsync(services, "c-missing").ConfigureAwait(false);
    // (a) the wait step: image_unavailable, and the step detail names the resolved id.
    var wait = WaitStepItems(details).Should().ContainSingle().Subject;
    AttributeText(wait, "reasonCode").Should().Be("image_unavailable");
    AttributeText(wait, "referenceImageId").Should().Be("no-such-image");
    ShouldRecordValue(ParameterItems(details, 1).Single(), "no-such-image");

    // (b) the tap step is not executed. The reason is different on different hosts, so the test
    // asserts only the status. No tap item is in the log, so no device input occurred.
    AttributeText(TapNotExecutedItem(details), "reasonCode").Should().Be("skipped_invalid_config");
    details.Should().NotContain(d => string.Equals(d.Kind, "tap", StringComparison.Ordinal));
    ShouldRecordValue(ParameterItems(details, 0).Single(), "no-such-image");
  }

  [Fact]
  public async Task NameWithNoValueGivesSkippedParameterUnresolved() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, OverlayCommand("c-unresolved", required: false), SequenceInvoking("s-unresolved", "c-unresolved"))
        .ConfigureAwait(false);

    await RunEntryAsync(services, "q-unresolved", "s-unresolved", value: null).ConfigureAwait(false);

    var details = await CommandDetailsAsync(services, "c-unresolved").ConfigureAwait(false);
    var tap = TapNotExecutedItem(details);
    AttributeText(tap, "reasonCode").Should().Be("skipped_parameter_unresolved");
    AttributeText(tap, "reason").Should().Be(
        $"Step '0': parameter 'novaOption' used by field '{TapImageKey}' could not be resolved from any scope.");
    details.Should().NotContain(d => string.Equals(d.Kind, "tap", StringComparison.Ordinal));
    details.Should().NotContain(d => string.Equals(d.Kind, "parameters", StringComparison.Ordinal));
  }

  [Fact]
  public async Task LiteralBindingWithNoImageIsNotExecuted() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    var bindings = new Collection<ParameterBinding> { new() { Name = "novaOption", Value = "no-such-bound-image" } };
    await SeedAsync(services, OverlayCommand("c-bound", required: false), SequenceInvoking("s-bound", "c-bound", bindings))
        .ConfigureAwait(false);

    await RunEntryAsync(services, "q-bound", "s-bound", value: null).ConfigureAwait(false);

    var details = await CommandDetailsAsync(services, "c-bound").ConfigureAwait(false);
    AttributeText(TapNotExecutedItem(details), "reasonCode").Should().Be("skipped_invalid_config");
    details.Should().NotContain(d => string.Equals(d.Kind, "tap", StringComparison.Ordinal));
    ShouldRecordValue(ParameterItems(details, 0).Single(), "no-such-bound-image", ParameterScopeLayers.Command);
  }
}
