using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Images;
using GameBot.Domain.Parameters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.ContractTests.QueueTemplates;

/// <summary>
/// Feature 114 (FR-010): <c>POST /api/queue-templates</c> rejects a known value that goes to an
/// image field and names no image, with the code <c>unknown_image_reference</c>.
/// </summary>
public sealed class TemplateImageReferenceContractTests {
  private const string TapImageKey = "primitiveTap.detectionTarget.referenceImageId";

  private const string OneByOnePngBase64 =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO2n5u4AAAAASUVORK5CYII=";

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static System.Net.Http.HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

  /// <summary>Stores a reference image through the repository. The upload route is not available on each host.</summary>
  private static async Task RegisterImageAsync(WebApplicationFactory<Program> app, string imageId) {
    var images = app.Services.GetRequiredService<IImageRepository>();
    using var content = new System.IO.MemoryStream(Convert.FromBase64String(OneByOnePngBase64));
    await images.SaveAsync(imageId, content, "image/png", $"{imageId}.png", overwrite: true).ConfigureAwait(false);
  }

  private static ParameterDeclaration Declare(string name, string? defaultValue = null) =>
      new() { Name = name, Default = defaultValue };

  private static Collection<ParameterBinding>? Bindings(params (string Name, string? Value)[] bindings) {
    if (bindings.Length == 0) return null;
    var result = new Collection<ParameterBinding>();
    foreach (var (name, value) in bindings) result.Add(new ParameterBinding { Name = name, Value = value });
    return result;
  }

  /// <summary>A sequence with one tap step. Its guard looks for <paramref name="imageId"/>.</summary>
  private static async Task<string> SeedGuardSequenceAsync(
      WebApplicationFactory<Program> app, string imageId, params ParameterDeclaration[] declarations) {
    var sequence = new CommandSequence { Id = Unique("seq"), Name = "Guard" };
    foreach (var declaration in declarations) sequence.Parameters.Add(declaration);
    var action = new SequenceActionPayload { Type = "tap" };
    action.Parameters["x"] = 1;
    action.Parameters["y"] = 2;
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0, StepId = "tap-option", StepType = SequenceStepType.Action, Action = action,
        Condition = new ImageVisibleStepCondition { ImageId = imageId }
      }
    });
    await app.Services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
    return sequence.Id;
  }

  /// <summary>A command with one tap step. The overlay image key selects the image.</summary>
  private static Command TapCommand(string id, params ParameterDeclaration[] declarations) {
    var command = new Command { Id = id, Name = id };
    foreach (var declaration in declarations) command.Parameters.Add(declaration);
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("option-a") },
      FieldTemplates = new Dictionary<string, string> { [TapImageKey] = "{{novaOption}}" }
    });
    return command;
  }

  /// <summary>A sequence with one command step, with optional bindings.</summary>
  private static async Task<string> SeedCallingSequenceAsync(
      WebApplicationFactory<Program> app,
      string commandId,
      Collection<ParameterBinding>? bindings,
      params ParameterDeclaration[] declarations) {
    var sequence = new CommandSequence { Id = Unique("seq"), Name = "Caller" };
    foreach (var declaration in declarations) sequence.Parameters.Add(declaration);
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0, StepId = "call", StepType = SequenceStepType.Command, CommandId = commandId, ParameterBindings = bindings
      }
    });
    await app.Services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
    return sequence.Id;
  }

  private static async Task SeedCommandAsync(WebApplicationFactory<Program> app, Command command) =>
      await app.Services.GetRequiredService<ICommandRepository>().AddAsync(command).ConfigureAwait(false);

  private static object Template(string name, string sequenceId, string? value, bool enabled = true) => new {
    name,
    overwrite = true,
    entries = new object[] {
      new {
        sequenceId,
        scheduleType = "OncePerRun",
        enabled,
        parameterValues = value is null ? Array.Empty<object>() : new object[] { new { name = "novaOption", value } }
      }
    }
  };

  private static async Task<JsonElement> ShouldBeRejectedAsync(
      System.Net.Http.HttpClient client, System.Net.Http.HttpResponseMessage response, string templateName) {
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
    var root = JsonDocument.Parse(body).RootElement;
    root.GetProperty("error").GetString().Should().Be("unknown_image_reference");

    var list = await client.GetAsync(new Uri("/api/queue-templates", UriKind.Relative)).ConfigureAwait(false);
    var templates = JsonDocument.Parse(await list.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    templates.EnumerateArray().Should().NotContain(t => t.GetProperty("name").GetString() == templateName,
        "the service saves nothing");
    return root;
  }

  private static async Task ShouldBeSavedAsync(System.Net.Http.HttpResponseMessage response) {
    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.Created, HttpStatusCode.OK }, body);
  }

  [Fact]
  public async Task EntryValueWithNoImageIsRejectedAndNothingIsStored() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await SeedGuardSequenceAsync(app, "{{novaOption}}", Declare("novaOption")).ConfigureAwait(false);
    var name = Unique("tpl");

    var response = await client.PostAsJsonAsync("/api/queue-templates", Template(name, sequenceId, "no-such-image"))
        .ConfigureAwait(false);

    var root = await ShouldBeRejectedAsync(client, response, name).ConfigureAwait(false);
    const string expected =
        "Entry 0: parameter 'novaOption' gives the image id 'no-such-image' to field 'condition.imageId', but no image has that id.";
    root.GetProperty("message").GetString().Should().Be(expected);
    var detail = root.GetProperty("details").EnumerateArray().Single();
    detail.GetProperty("code").GetString().Should().Be("unknown_image_reference");
    detail.GetProperty("message").GetString().Should().Be(expected);
    detail.GetProperty("fieldPath").GetString().Should().Be("condition.imageId");
    detail.GetProperty("parameterName").GetString().Should().Be("novaOption");
  }

  [Fact]
  public async Task DefaultWithNoImageIsRejected() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await SeedGuardSequenceAsync(app, "{{novaOption}}", Declare("novaOption", "missing-default-114"))
        .ConfigureAwait(false);
    var name = Unique("tpl");

    var response = await client.PostAsJsonAsync("/api/queue-templates", Template(name, sequenceId, null))
        .ConfigureAwait(false);

    var root = await ShouldBeRejectedAsync(client, response, name).ConfigureAwait(false);
    root.GetProperty("message").GetString().Should().Contain("'missing-default-114'");
  }

  [Fact]
  public async Task DisabledEntryIsChecked() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await SeedGuardSequenceAsync(app, "{{novaOption}}", Declare("novaOption")).ConfigureAwait(false);
    var name = Unique("tpl");

    var response = await client.PostAsJsonAsync("/api/queue-templates",
        Template(name, sequenceId, "no-such-image", enabled: false)).ConfigureAwait(false);

    await ShouldBeRejectedAsync(client, response, name).ConfigureAwait(false);
  }

  [Fact]
  public async Task ValueThatNamesAnImageIsSaved() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    await RegisterImageAsync(app, "option-b-114").ConfigureAwait(false);
    var sequenceId = await SeedGuardSequenceAsync(app, "{{novaOption}}", Declare("novaOption")).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/queue-templates",
        Template(Unique("tpl"), sequenceId, "option-b-114")).ConfigureAwait(false);

    await ShouldBeSavedAsync(response).ConfigureAwait(false);
  }

  [Fact]
  public async Task ValueThroughACommandImageKeyIsChecked() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var commandId = Unique("cmd");
    await SeedCommandAsync(app, TapCommand(commandId, Declare("novaOption"))).ConfigureAwait(false);
    var sequenceId = await SeedCallingSequenceAsync(app, commandId, null).ConfigureAwait(false);
    var name = Unique("tpl");

    var response = await client.PostAsJsonAsync("/api/queue-templates", Template(name, sequenceId, "no-such-image"))
        .ConfigureAwait(false);

    var root = await ShouldBeRejectedAsync(client, response, name).ConfigureAwait(false);
    root.GetProperty("details")[0].GetProperty("fieldPath").GetString().Should().Be(TapImageKey);
  }

  [Fact]
  public async Task QueueBuiltInIsNotChecked() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await SeedGuardSequenceAsync(app, "{{queue.gameId}}").ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/queue-templates",
        Template(Unique("tpl"), sequenceId, null)).ConfigureAwait(false);

    await ShouldBeSavedAsync(response).ConfigureAwait(false);
  }

  [Fact]
  public async Task EntryWhoseSequenceDoesNotExistIsNotChecked() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/queue-templates",
        Template(Unique("tpl"), Unique("no-sequence"), "no-such-image")).ConfigureAwait(false);

    await ShouldBeSavedAsync(response).ConfigureAwait(false);
  }

  [Fact]
  public async Task LiteralBindingOnASequenceStepIsNotChecked() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var commandId = Unique("cmd");
    await SeedCommandAsync(app, TapCommand(commandId, Declare("novaOption"))).ConfigureAwait(false);
    var sequenceId = await SeedCallingSequenceAsync(app, commandId, Bindings(("novaOption", "no-such-bound-image")))
        .ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/queue-templates",
        Template(Unique("tpl"), sequenceId, null)).ConfigureAwait(false);

    await ShouldBeSavedAsync(response).ConfigureAwait(false);
  }

  [Fact]
  public async Task StepBindingOutranksTheEntryValue() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    await RegisterImageAsync(app, "option-b-114").ConfigureAwait(false);
    var commandId = Unique("cmd");
    await SeedCommandAsync(app, TapCommand(commandId, Declare("novaOption"))).ConfigureAwait(false);
    var sequenceId = await SeedCallingSequenceAsync(app, commandId, Bindings(("novaOption", "option-b-114")))
        .ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/queue-templates",
        Template(Unique("tpl"), sequenceId, "no-such-image")).ConfigureAwait(false);

    await ShouldBeSavedAsync(response).ConfigureAwait(false);
  }

  [Fact]
  public async Task CommandDefaultIsCheckedBeforeTheSequenceDefault() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    await RegisterImageAsync(app, "option-b-114").ConfigureAwait(false);
    var commandId = Unique("cmd");
    await SeedCommandAsync(app, TapCommand(commandId, Declare("novaOption", "missing-command-default-114")))
        .ConfigureAwait(false);
    var sequenceId = await SeedCallingSequenceAsync(app, commandId, null, Declare("novaOption", "option-b-114"))
        .ConfigureAwait(false);
    var name = Unique("tpl");

    var response = await client.PostAsJsonAsync("/api/queue-templates", Template(name, sequenceId, null))
        .ConfigureAwait(false);

    var root = await ShouldBeRejectedAsync(client, response, name).ConfigureAwait(false);
    var detail = root.GetProperty("details").EnumerateArray().Single();
    detail.GetProperty("fieldPath").GetString().Should().Be(TapImageKey);
    detail.GetProperty("message").GetString().Should().Contain("'missing-command-default-114'");
  }

  [Fact]
  public async Task NestedStepBindingOutranksTheEntryValue() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    await RegisterImageAsync(app, "option-b-114").ConfigureAwait(false);
    var innerId = Unique("inner");
    await SeedCommandAsync(app, TapCommand(innerId, Declare("novaOption"))).ConfigureAwait(false);
    var outer = new Command { Id = Unique("outer"), Name = "outer" };
    outer.Steps.Add(new CommandStep {
      Type = CommandStepType.Command,
      Order = 0,
      TargetId = innerId,
      ParameterBindings = Bindings(("novaOption", "option-b-114"))
    });
    await SeedCommandAsync(app, outer).ConfigureAwait(false);
    var sequenceId = await SeedCallingSequenceAsync(app, outer.Id, null).ConfigureAwait(false);

    var response = await client.PostAsJsonAsync("/api/queue-templates",
        Template(Unique("tpl"), sequenceId, "no-such-image")).ConfigureAwait(false);

    await ShouldBeSavedAsync(response).ConfigureAwait(false);
  }
}
