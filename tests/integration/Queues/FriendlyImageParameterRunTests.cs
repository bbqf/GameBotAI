using System;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GameBot.IntegrationTests.Queues.FriendlyRunHelpers;

namespace GameBot.IntegrationTests.Queues;

/// <summary>Feature 168: an image parameter gives the image ID to the step, and a stale image never stops the queue start.</summary>
[Collection("ConfigIsolation")]
public sealed class FriendlyImageParameterRunTests {
  public FriendlyImageParameterRunTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static ParameterDeclaration Pic() => new() {
    Name = "pic", Kind = ParameterInputKind.Image, AllowedImages = new[] { "img-a", "img-b" }
  };

  [Fact]
  public async Task SelectedAllowedImageReachesTheStepAsItsId() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(
        services, KeyCommand("c-img1", "{{pic}}", Pic()), SequenceInvoking("s-img1", "c-img1"), "q-img1",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "pic", Value = "img-b" }))
      .ConfigureAwait(false);

    await RunToCompletionAsync(services.GetRequiredService<IQueueExecutionService>(), "q-img1").ConfigureAwait(false);

    var (text, data) = await ParameterLogAsync(services.GetRequiredService<IExecutionLogService>()).ConfigureAwait(false);
    text.Should().Contain("pic=img-b");
    data.Should().NotContain("label", "only a choice has a label");
  }

  [Fact]
  public async Task ImageThatIsNotAllowedAnyMoreDoesNotRefuseTheQueueStart() {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    var services = app.Services;
    await SeedAsync(
        services, KeyCommand("c-img2", "{{pic}}", Pic()), SequenceInvoking("s-img2", "c-img2"), "q-img2",
        entry => entry.ParameterValues.Add(new ParameterBinding { Name = "pic", Value = "removed-image" }))
      .ConfigureAwait(false);

    var response = await client.PostAsync(new Uri("/api/queues/q-img2/start", UriKind.Relative), content: null)
        .ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }
}
