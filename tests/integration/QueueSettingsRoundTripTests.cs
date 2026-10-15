using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.QueueSettings;
using GameBot.UnitTests.Gamepacks;
using GameBot.UnitTests.QueueSettings.TestDoubles;
using Xunit;

#pragma warning disable CA2007, CA1707, CA1861, CA2000, CA1812

namespace GameBot.IntegrationTests;

/// <summary>
/// Feature 145 (T026): the export and the import with real stores and a real gamepack. The HTTP calls go through a
/// counting handler, so the test proves that one export request and one import request restore the settings (SC-006).
/// </summary>
public sealed class QueueSettingsRoundTripTests {
  private static string Pack(string version, string commandName = "PackCmd") => GamepackTestKit.Pack(
    "pk",
    version,
    sequences: new[] { GamepackTestKit.Seq("ps", "PackSeq", "pc") },
    commands: new[] { GamepackTestKit.Cmd("pc", commandName) });

  private static async Task SeedAsync(QueueSettingsTestKit kit) {
    var packCommand = (await kit.Packs.CommandByNameAsync("PackCmd"))!;
    var packSequence = (await kit.Packs.SequenceByNameAsync("PackSeq"))!;
    var own = await kit.AddCommandAsync("OwnCmd");
    var mine = await kit.AddSequenceAsync("Mine", own.Id, packCommand.Id);
    var template = await kit.AddTemplateAsync("Daily", packSequence.Id, mine.Id);
    await kit.AddQueueAsync("Q1", template.Id, null, packSequence.Id, mine.Id);
  }

  /// <summary>Sends the export and import requests of the kit through a counting handler.</summary>
  private sealed class CountingHandler : HttpMessageHandler {
    private readonly QueueSettingsTestKit _source;
    private readonly QueueSettingsTestKit _target;

    public CountingHandler(QueueSettingsTestKit source, QueueSettingsTestKit target) {
      _source = source;
      _target = target;
    }

    public List<string> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
      Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
      if (request.Method == HttpMethod.Get && request.RequestUri.AbsolutePath == "/api/queue-settings/export") {
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(await _source.ExportJsonAsync(), Encoding.UTF8, "application/json") };
      }

      if (request.Method == HttpMethod.Post && request.RequestUri.AbsolutePath == "/api/queue-settings/import") {
        var outcome = await _target.Importer.ImportAsync(await request.Content!.ReadAsStringAsync(cancellationToken), cancellationToken);
        return new HttpResponseMessage(outcome.Status == ImportStatus.Ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
      }

      return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
  }

  [Fact]
  public async Task OneExportAndOneImportRestoreTheSettingsByName() {
    using var a = new QueueSettingsTestKit();
    using var b = new QueueSettingsTestKit();
    Assert.Equal(GamepackInstallStatus.Ok, (await a.Packs.Service.InstallAsync(Pack("1.0.0"), false)).Status);
    Assert.Equal(GamepackInstallStatus.Ok, (await b.Packs.Service.InstallAsync(Pack("1.0.0"), false)).Status);
    await SeedAsync(a);
    using var handler = new CountingHandler(a, b);
    using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };

    var export = await client.GetAsync(new Uri("/api/queue-settings/export", UriKind.Relative));
    var import = await client.PostAsync(new Uri("/api/queue-settings/import", UriKind.Relative), new StringContent(await export.Content.ReadAsStringAsync(), Encoding.UTF8, "application/json"));

    Assert.Equal(HttpStatusCode.OK, import.StatusCode);
    Assert.Equal(new[] { "GET /api/queue-settings/export", "POST /api/queue-settings/import" }, handler.Requests.ToArray());
    var packSequence = (await b.Packs.SequenceByNameAsync("PackSeq"))!;
    var packCommand = (await b.Packs.CommandByNameAsync("PackCmd"))!;
    var own = (await b.Packs.CommandByNameAsync("OwnCmd"))!;
    var mine = (await b.Packs.SequenceByNameAsync("Mine"))!;
    Assert.Equal(new[] { own.Id, packCommand.Id }, mine.Steps.Select(s => s.CommandId).ToArray());
    var template = (await b.TemplateByNameAsync("Daily"))!;
    Assert.Equal(new[] { packSequence.Id, mine.Id }, template.Entries.Select(e => e.SequenceId).ToArray());
    var queue = (await b.QueueByNameAsync("Q1"))!;
    Assert.Equal(template.Id, queue.LinkedTemplateId);
    Assert.Equal(new[] { packSequence.Id, mine.Id }, b.Runtime.GetEntries(queue.Id).Select(e => e.SequenceId).ToArray());
  }

  [Fact]
  public async Task GamepackUpdateDoesNotChangeOwnedObjectsOnImport() {
    using var a = new QueueSettingsTestKit();
    using var b = new QueueSettingsTestKit();
    await a.Packs.Service.InstallAsync(Pack("1.0.0"), false);
    await b.Packs.Service.InstallAsync(Pack("1.0.0"), false);
    await SeedAsync(a);
    var json = await a.ExportJsonAsync();
    // The target runs a newer gamepack version with a changed command name for the same object.
    await b.Packs.Service.InstallAsync(Pack("2.0.0"), false);
    var ownedBefore = b.Packs.Snapshot();

    var outcome = await b.Importer.ImportAsync(json);

    Assert.Equal(ImportStatus.Ok, outcome.Status);
    Assert.Empty(outcome.Report!.UnresolvedReferences);
    var after = b.Packs.Snapshot();
    var registryFiles = ownedBefore.Keys.Where(k => k.Contains("gamepacks", StringComparison.OrdinalIgnoreCase)).ToList();
    Assert.NotEmpty(registryFiles);
    foreach (var file in registryFiles) {
      Assert.Equal(ownedBefore[file], after[file]);
    }

    var owner = b.Packs.OwnerOf(GamepackObjectTypes.Sequence, (await b.Packs.SequenceByNameAsync("PackSeq"))!.Id);
    Assert.Equal("2.0.0", owner.Version);
  }

  [Fact]
  public async Task MissingGamepackGivesUnresolvedListAndTheRestIsApplied() {
    using var a = new QueueSettingsTestKit();
    using var b = new QueueSettingsTestKit();
    await a.Packs.Service.InstallAsync(Pack("1.0.0"), false);
    await SeedAsync(a);
    var json = await a.ExportJsonAsync();

    var outcome = await b.Importer.ImportAsync(json);

    Assert.Equal(ImportStatus.Ok, outcome.Status);
    var report = outcome.Report!;
    Assert.Contains(report.UnresolvedReferences, r => r.Type == "sequence" && r.Name == "PackSeq" && r.Gamepack == "pk" && r.Version == "1.0.0");
    Assert.Contains(report.UnresolvedReferences, r => r.Type == "command" && r.Name == "PackCmd" && r.Gamepack == "pk");
    Assert.NotNull(await b.QueueByNameAsync("Q1"));
    Assert.NotNull(await b.Packs.SequenceByNameAsync("Mine"));
  }
}
