using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GameBot.ContractTests.Gamepacks;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.QueueSettings;

/// <summary>Feature 145 (T023): the import route.</summary>
public sealed class QueueSettingsImportContractTests {
  private static Task<HttpResponseMessage> ImportAsync(GamepackContractHost host, string json) =>
    host.Client.PostAsync(new Uri("/api/queue-settings/import", UriKind.Relative), new StringContent(json, Encoding.UTF8, "application/json"));

  private static string SmallDocument() => QueueSettingsTestKit.Doc(
    queues: QueueSettingsTestKit.List(QueueSettingsTestKit.QueueJson("Q1")),
    sequences: QueueSettingsTestKit.List(QueueSettingsTestKit.SequenceJson("S1", "C1")),
    commands: QueueSettingsTestKit.List(QueueSettingsTestKit.CommandJsonText("C1")));

  [Fact]
  public async Task ValidDocumentGives200WithTheReport() {
    using var host = new GamepackContractHost();

    var response = await ImportAsync(host, SmallDocument());

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("items").GetArrayLength().Should().Be(3);
    body.GetProperty("unresolvedReferences").GetArrayLength().Should().Be(0);
    body.GetProperty("summary").GetProperty("created").GetInt32().Should().Be(3);
    body.TryGetProperty("failed", out _).Should().BeFalse();
    body.GetProperty("items")[0].GetProperty("outcome").GetString().Should().Be("created");
  }

  [Fact]
  public async Task InvalidDocumentGives400AndChangesNothing() {
    using var host = new GamepackContractHost();

    var response = await ImportAsync(host, QueueSettingsTestKit.Doc(formatVersion: 2));

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("detail").GetString().Should().Contain("not supported");
    (await host.GetJsonAsync("/api/queues")).GetArrayLength().Should().Be(0);
  }

  [Fact]
  public async Task BodyThatIsNotJsonGives400() {
    using var host = new GamepackContractHost();

    var response = await ImportAsync(host, "this is not json");

    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
  }

  [Fact]
  public async Task BodyLargerThanTheLimitGives413() {
    using var host = new GamepackContractHost();

    var response = await host.Client.PostAsync(new Uri("/api/queue-settings/import", UriKind.Relative), new FakeLengthContent(21L * 1024 * 1024));

    response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
  }

  [Fact]
  public async Task FailingRepositoryGives500WithAPartialReport() {
    var probe = new WriteProbe { FailAtWrite = 1 };
    using var host = new GamepackContractHost(configure: (services, root) => {
      services.RemoveAll<IQueueRepository>();
      services.AddSingleton<IQueueRepository>(_ => new ProbedQueueRepository(new FileQueueRepository(root), probe));
    });

    // The probe sees only queue writes. The first queue write fails. The command and the sequence are written before it.
    var response = await ImportAsync(host, SmallDocument());

    response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("result").GetString().Should().Be("partial_import");
    var report = body.GetProperty("report");
    report.GetProperty("items").GetArrayLength().Should().Be(2);
    report.GetProperty("failed").GetProperty("name").GetString().Should().Be("Q1");
    report.GetProperty("notApplied").GetArrayLength().Should().Be(0);
    (await host.GetJsonAsync("/api/sequences")).GetArrayLength().Should().Be(1);
  }

  [Fact]
  public async Task ExportOnHostAImportOnHostBGivesTheSameSettingsByName() {
    var pack = GamepackTestKit.Pack("pk", "1.0.0",
      sequences: new[] { GamepackTestKit.Seq("ps", "PackSeq", "pc") },
      commands: new[] { GamepackTestKit.Cmd("pc", "PackCmd") });
    using var hostA = new GamepackContractHost();
    using var hostB = new GamepackContractHost();
    (await hostA.InstallAsync(pack)).StatusCode.Should().Be(HttpStatusCode.OK);
    (await hostB.InstallAsync(pack)).StatusCode.Should().Be(HttpStatusCode.OK);
    await SeedAsync(hostA);

    var export = await hostA.Client.GetAsync(new Uri("/api/queue-settings/export", UriKind.Relative));
    var json = await export.Content.ReadAsStringAsync();
    var import = await ImportAsync(hostB, json);

    import.StatusCode.Should().Be(HttpStatusCode.OK);
    var report = await GamepackContractHost.ReadAsync(import);
    report.GetProperty("unresolvedReferences").GetArrayLength().Should().Be(0);
    var sequences = hostB.Services.GetRequiredService<ISequenceRepository>();
    var commands = hostB.Services.GetRequiredService<ICommandRepository>();
    var templates = hostB.Services.GetRequiredService<IQueueTemplateRepository>();
    var queues = hostB.Services.GetRequiredService<IQueueRepository>();
    var runtime = hostB.Services.GetRequiredService<IQueueRuntimeStore>();
    var packCommand = (await commands.ListAsync()).Single(c => c.Name == "PackCmd");
    var packSequence = (await sequences.ListAsync()).Single(s => s.Name == "PackSeq");
    var mine = (await sequences.ListAsync()).Single(s => s.Name == "Mine");
    mine.Steps.Single().CommandId.Should().Be(packCommand.Id);
    var template = await templates.FindByNameAsync("Daily");
    template!.Entries.Select(e => e.SequenceId).Should().Equal(packSequence.Id, mine.Id);
    var queue = (await queues.ListAsync()).Single(q => q.Name == "Q1");
    queue.LinkedTemplateId.Should().Be(template.Id);
    runtime.GetEntries(queue.Id).Select(e => e.SequenceId).Should().Equal(packSequence.Id, mine.Id);

    var again = await ImportAsync(hostB, json);
    var second = await GamepackContractHost.ReadAsync(again);
    second.GetProperty("summary").GetProperty("unchanged").GetInt32().Should().Be(3);
  }

  private static async Task SeedAsync(GamepackContractHost host) {
    var commands = host.Services.GetRequiredService<ICommandRepository>();
    var sequences = host.Services.GetRequiredService<ISequenceRepository>();
    var templates = host.Services.GetRequiredService<IQueueTemplateRepository>();
    var queues = host.Services.GetRequiredService<IQueueRepository>();
    var runtime = host.Services.GetRequiredService<IQueueRuntimeStore>();
    var packCommand = (await commands.ListAsync()).Single(c => c.Name == "PackCmd");
    var packSequence = (await sequences.ListAsync()).Single(s => s.Name == "PackSeq");
    var mine = new CommandSequence { Name = "Mine", Version = 1 };
    mine.SetSteps(new[] { new SequenceStep { Order = 0, StepId = "s0", CommandId = packCommand.Id, StepType = SequenceStepType.Command } });
    mine = await sequences.CreateAsync(mine);
    var template = new QueueTemplate { Name = "Daily" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = packSequence.Id });
    template.Entries.Add(new QueueTemplateEntry { SequenceId = mine.Id });
    template = await templates.CreateAsync(template);
    var queue = await queues.CreateAsync(new ExecutionQueue { Name = "Q1", EmulatorSerial = "emulator-5554", LinkedTemplateId = template.Id });
    runtime.SetEntries(queue.Id, new[] { packSequence.Id, mine.Id });
  }
}
