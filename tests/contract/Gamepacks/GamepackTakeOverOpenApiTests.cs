using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 149 (T049, FR-015): the OpenAPI document describes the take-over option, the list, the count, the backup id, and the restore route.</summary>
public sealed class GamepackTakeOverOpenApiTests {
  private static async Task<JsonElement> ReadDocumentAsync() {
    using var host = new GamepackContractHost();
    var response = await host.Client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.Clone();
  }

  private static string[] ParameterNames(JsonElement operation) =>
    operation.GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray();

  [Fact]
  public async Task TheInstallRoutesHaveTheTakeOverOption() {
    var paths = (await ReadDocumentAsync()).GetProperty("paths");

    var install = paths.GetProperty("/api/gamepacks/install").GetProperty("post");
    ParameterNames(install).Should().Contain("takeOver");
    install.GetProperty("description").GetString().Should().Contain("takeOver=true").And.Contain("takeOvers").And.Contain("takeOverBackupId");

    var update = paths.GetProperty("/api/gamepacks/update").GetProperty("post");
    ParameterNames(update).Should().Contain("takeOver");
    update.GetProperty("description").GetString().Should().Contain("takeOver=true").And.Contain("takeover_restore");

    var rollback = paths.GetProperty("/api/gamepacks/{name}/rollback").GetProperty("post");
    ParameterNames(rollback).Should().NotContain("takeOver", "a rollback never takes an object over");
  }

  [Fact]
  public async Task TheRestoreRouteIsDescribedWithItsResponsesAndReasonCodes() {
    var paths = (await ReadDocumentAsync()).GetProperty("paths");

    var restore = paths.GetProperty("/api/gamepacks/takeover-backups/{backupId}/restore").GetProperty("post");

    restore.GetProperty("operationId").GetString().Should().Be("RestoreGamepackTakeOverBackup");
    foreach (var status in new[] { "200", "404", "409", "500" }) {
      restore.GetProperty("responses").TryGetProperty(status, out _).Should().BeTrue("the restore route lists " + status);
    }

    restore.GetProperty("description").GetString().Should().Contain("object_missing").And.Contain("object_changed").And.Contain("owner_changed").And.Contain("second restore");
  }

  [Fact]
  public async Task TheSchemasHaveTheNewFieldsWithDescriptions() {
    var schemas = (await ReadDocumentAsync()).GetProperty("components").GetProperty("schemas");

    var install = schemas.GetProperty("GamepackInstallResponse").GetProperty("properties");
    foreach (var name in new[] { "takenOver", "takeOverBackupId", "takeOvers" }) {
      install.GetProperty(name).GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(name);
    }

    var report = schemas.GetProperty("ManagedUpdateReportDto").GetProperty("properties");
    foreach (var name in new[] { "takenOver", "takeOverBackupId", "takeOvers" }) {
      report.GetProperty(name).GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace(name);
    }

    schemas.GetProperty("GamepackTakeOverEntryDto").GetProperty("properties").GetProperty("identical").GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    schemas.GetProperty("GamepackTakeOverRestoreReasonDto").GetProperty("properties").GetProperty("code").GetProperty("description").GetString()
      .Should().Contain("object_missing").And.Contain("object_changed").And.Contain("owner_changed");
    schemas.GetProperty("GamepackCatalogInstallRequest").GetProperty("properties").GetProperty("takeOver").GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
    schemas.GetProperty("GamepackCatalogUpdateRequest").GetProperty("properties").GetProperty("takeOver").GetProperty("description").GetString().Should().NotBeNullOrWhiteSpace();
  }
}
