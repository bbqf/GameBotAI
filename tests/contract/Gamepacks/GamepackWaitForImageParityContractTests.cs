using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.UnitTests.Gamepacks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using W = GameBot.UnitTests.Gamepacks.WaitForImagePackFixtures;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310, CA1849

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 177 (SC-003): a pack install and POST /api/sequences store the same step for the same payload.</summary>
public sealed class GamepackWaitForImageParityContractTests {
  private const string SavedName = "Saved by the API";

  private static string Json(object? value) => JsonSerializer.Serialize(value);

  private static async Task<SequenceStep> SavedStepAsync(GamepackContractHost host, string payload, bool nested) {
    var inner = "{\"stepId\":\"target\",\"stepType\":\"Action\",\"primitiveAction\":{\"type\":\"WaitForImage\",\"payload\":" + payload + "}}";
    var step = nested ? W.Loop("outer", inner) : inner;
    var body = "{\"name\":\"" + SavedName + "\",\"version\":1,\"steps\":[" + step + "]}";
    using var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
    var response = await host.Client.PostAsync(new Uri("/api/sequences", UriKind.Relative), content);
    var text = await response.Content.ReadAsStringAsync();
    response.StatusCode.Should().Be(HttpStatusCode.Created, text);
    var saved = await GamepackWaitForImageInstallContractTests.StoredAsync(host, SavedName);
    return GamepackWaitForImageInstallContractTests.Find(saved.Steps, "target");
  }

  private static async Task<SequenceStep> InstalledStepAsync(GamepackContractHost host, string payload, bool nested) {
    var inner = W.Step("target", payload);
    var response = await host.InstallAsync(W.PackWith("1.0.0", nested ? W.Loop("outer", inner) : inner));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var installed = await GamepackWaitForImageInstallContractTests.StoredAsync(host);
    return GamepackWaitForImageInstallContractTests.Find(installed.Steps, "target");
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task TheSameFullPayloadGivesTheSameStoredStep(bool nested) {
    using var host = new GamepackContractHost();
    var payload = "{\"timeoutMs\":4000,\"until\":\"absent\",\"absentCaptures\":3,\"detectionTarget\":{\"referenceImageId\":\"" + W.ImageId
      + "\",\"confidence\":0.9,\"offsetX\":4,\"offsetY\":-2,\"selectionStrategy\":\"FirstMatch\",\"region\":{\"x\":10,\"y\":20,\"width\":300,\"height\":200}}}";

    var installed = await InstalledStepAsync(host, payload, nested);
    var saved = await SavedStepAsync(host, payload, nested);

    installed.WaitForImage.Should().NotBeNull();
    Json(installed.WaitForImage).Should().Be(Json(saved.WaitForImage));
    installed.Action!.Type.Should().Be(saved.Action!.Type);
    Json(installed.Action.Parameters).Should().Be(Json(saved.Action.Parameters));
    var a = installed.WaitForImage!.DetectionTarget!;
    var b = saved.WaitForImage!.DetectionTarget!;
    (a.ReferenceImageId, a.Confidence, a.OffsetX, a.OffsetY, a.SelectionStrategy).Should().Be((b.ReferenceImageId, b.Confidence, b.OffsetX, b.OffsetY, b.SelectionStrategy));
    (a.Region!.X, a.Region.Y, a.Region.Width, a.Region.Height).Should().Be((b.Region!.X, b.Region.Y, b.Region.Width, b.Region.Height));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task TheSameDefaultPayloadGivesTheSameStoredStep(bool nested) {
    using var host = new GamepackContractHost();
    var payload = "{\"detectionTarget\":{\"referenceImageId\":\"" + W.ImageId + "\"}}";

    var installed = await InstalledStepAsync(host, payload, nested);
    var saved = await SavedStepAsync(host, payload, nested);

    Json(installed.WaitForImage).Should().Be(Json(saved.WaitForImage));
    installed.WaitForImage!.TimeoutMs.Should().Be(1000);
  }
}
