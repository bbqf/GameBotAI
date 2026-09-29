using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Issue #226: the queue-template endpoint and the sequence validator must accept and reject the
/// same <c>timerTimeOfDay</c> strings, and both error messages must name the same format.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class TimerTimeOfDayParityTests {
  private const string FormatPhrase = "HH:mm or HH:mm:ss";

  public static TheoryData<string, bool> Values() => new() {
    { "00:00", true },
    { "09:05", true },
    { "15:30", true },
    { "23:59", true },
    { "00:00:00", true },
    { "15:30:45", true },
    { "23:59:59", true },
    { "24:00", false },
    { "24:00:00", false },
    { "23:60", false },
    { "12:00:60", false },
    { "9:30", false },
    { "11:00 PM", false },
    { "5pm", false },
    { " 15:30", false },
    { "15:30 ", false },
    { "15.30", false },
    { "1530", false },
    { "15:30:45.123", false },
    { "abc", false }
  };

  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
    return new WebApplicationFactory<Program>();
  }

  private static HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
    return client;
  }

  [Theory]
  [MemberData(nameof(Values))]
  public async Task TemplateAndSequenceGiveTheSameResult(string value, bool expectedAccepted) {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var templateResp = await client.PostAsJsonAsync(new Uri("/api/queue-templates", UriKind.Relative), new {
      name = "Parity",
      overwrite = false,
      entries = new[] { new { sequenceId = "seq-a", scheduleType = "Timer", timerTimeOfDay = value } }
    }).ConfigureAwait(true);

    var sequenceResp = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), new {
      name = "parity-seq",
      version = 1,
      dryRun = true,
      steps = new object[] {
        new {
          stepId = "r1",
          label = "Reschedule",
          stepType = "Action",
          primitiveAction = new {
            type = "reschedule-self",
            schemaVersion = "v1",
            payload = new { option = "Timer", timerTimeOfDay = value }
          }
        }
      }
    }).ConfigureAwait(true);

    var templateAccepted = templateResp.StatusCode == HttpStatusCode.Created;
    var sequenceAccepted = sequenceResp.StatusCode == HttpStatusCode.OK;
    templateAccepted.Should().Be(sequenceAccepted, "both validators use one rule for '{0}'", value);
    templateAccepted.Should().Be(expectedAccepted, "the rule for '{0}' is fixed", value);

    if (!expectedAccepted) {
      var templateBody = await templateResp.Content.ReadAsStringAsync().ConfigureAwait(true);
      var sequenceBody = await sequenceResp.Content.ReadAsStringAsync().ConfigureAwait(true);
      templateBody.Should().Contain(FormatPhrase);
      sequenceBody.Should().Contain(FormatPhrase);
      using var doc = JsonDocument.Parse(sequenceBody);
      doc.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString()!)
        .Should().Contain(e => e.Contains(FormatPhrase, StringComparison.Ordinal));
    }
  }
}
