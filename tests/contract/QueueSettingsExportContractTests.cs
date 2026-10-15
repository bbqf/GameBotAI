using System.Net;
using FluentAssertions;
using GameBot.ContractTests.Gamepacks;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000

namespace GameBot.ContractTests.QueueSettings;

/// <summary>Feature 145 (T014): the export route.</summary>
public sealed class QueueSettingsExportContractTests {
  [Fact]
  public async Task ExportGives200WithFormatVersionOneAndTheFileHeader() {
    using var host = new GamepackContractHost();

    var response = await host.Client.GetAsync(new Uri("/api/queue-settings/export", UriKind.Relative));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    response.Content.Headers.ContentDisposition.FileName!.Trim('"').Should().Be("gamebot-settings.json");
    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("formatVersion").GetInt32().Should().Be(1);
    foreach (var list in new[] { "queues", "queueTemplates", "sequences", "commands", "references" }) {
      body.GetProperty(list).ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
    }
  }

  [Fact]
  public async Task ExportWithNoDataIsStillAValidDocument() {
    using var host = new GamepackContractHost();

    var response = await host.Client.GetAsync(new Uri("/api/queue-settings/export", UriKind.Relative));

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("queues").GetArrayLength().Should().Be(0);
    body.GetProperty("queueTemplates").GetArrayLength().Should().Be(0);
    body.GetProperty("references").GetArrayLength().Should().Be(0);
  }
}
