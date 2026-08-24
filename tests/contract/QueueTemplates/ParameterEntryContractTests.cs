using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.QueueTemplates;

/// <summary>
/// Feature 078: the queue-template entry's parameter members — supplied values, the override badge
/// flag, the effective-value preview, and the one blocking error code the save can return.
/// </summary>
public sealed class ParameterEntryContractTests {
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

  /// <summary>
  /// A template save with <c>overwrite: true</c> is create-or-replace, so it answers 201 the first
  /// time a name is used and 200 on every later run against the same (persistent) data directory.
  /// Asserting only 201 would make these tests pass once and fail forever after.
  /// </summary>
  private static void ShouldBeSaved(HttpResponseMessage response) =>
      response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

  /// <summary>Creates a sequence and returns its id, optionally declaring parameters.</summary>
  private static async Task<string> CreateSequenceAsync(
      System.Net.Http.HttpClient client, string name, params object[] parameters) {
    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name,
      version = 1,
      parameters,
      steps = new object[] {
        new { stepId = "s1", stepType = "Action", primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 2 } } }
      }
    }).ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created);
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("id").GetString()!;
  }

  [Fact]
  public async Task EntryParameterValuesRoundTripAndSetTheOverrideFlag() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client, "tpl-param-seq").ConfigureAwait(false);

    var save = await client.PostAsJsonAsync("/api/queue-templates", new {
      name = "Param Template",
      overwrite = true,
      entries = new object[] {
        new {
          sequenceId,
          scheduleType = "OncePerRun",
          parameterValues = new object[] { new { name = "adbSerial", value = "emulator-5560" } }
        }
      }
    }).ConfigureAwait(false);

    ShouldBeSaved(save);
    var entry = JsonDocument.Parse(await save.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("entries").EnumerateArray().Single();

    entry.GetProperty("hasParameterOverrides").GetBoolean().Should().BeTrue();
    var value = entry.GetProperty("parameterValues").EnumerateArray().Single();
    value.GetProperty("name").GetString().Should().Be("adbSerial");
    value.GetProperty("value").GetString().Should().Be("emulator-5560");
  }

  [Fact]
  public async Task AnEntryWithNoValuesReportsNoOverrideAndOmitsTheMember() {
    // FR-032: templates saved before the feature must not change shape.
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client, "tpl-plain-seq").ConfigureAwait(false);

    var save = await client.PostAsJsonAsync("/api/queue-templates", new {
      name = "Plain Template",
      overwrite = true,
      entries = new object[] { new { sequenceId, scheduleType = "OncePerRun" } }
    }).ConfigureAwait(false);

    ShouldBeSaved(save);
    var entry = JsonDocument.Parse(await save.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("entries").EnumerateArray().Single();

    entry.GetProperty("hasParameterOverrides").GetBoolean().Should().BeFalse();
    entry.TryGetProperty("parameterValues", out _)
        .Should().BeFalse("the member is omitted when the entry supplies nothing");
  }

  [Fact]
  public async Task EffectiveParametersPreviewShowsTheValueAndItsOriginatingScope() {
    // FR-028: the operator can see what a parameter resolves to, and which scope wins, without running.
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(
        client, "tpl-effective-seq",
        new { name = "adbSerial", @default = "emulator-5558" }).ConfigureAwait(false);

    var save = await client.PostAsJsonAsync("/api/queue-templates", new {
      name = "Effective Template",
      overwrite = true,
      entries = new object[] {
        new {
          sequenceId,
          scheduleType = "OncePerRun",
          parameterValues = new object[] { new { name = "adbSerial", value = "emulator-5560" } }
        }
      }
    }).ConfigureAwait(false);

    var entry = JsonDocument.Parse(await save.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("entries").EnumerateArray().Single();

    var effective = entry.GetProperty("effectiveParameters").EnumerateArray()
        .Single(e => e.GetProperty("name").GetString() == "adbSerial");
    effective.GetProperty("value").GetString().Should().Be("emulator-5560");
    effective.GetProperty("originLayer").GetString().Should().Be("entry",
        "an entry value outranks the sequence's declared default");
  }

  [Fact]
  public async Task EffectiveParametersFallBackToTheDeclaredDefault() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(
        client, "tpl-default-seq",
        new { name = "adbSerial", @default = "emulator-5558" }).ConfigureAwait(false);

    var save = await client.PostAsJsonAsync("/api/queue-templates", new {
      name = "Default Template",
      overwrite = true,
      entries = new object[] { new { sequenceId, scheduleType = "OncePerRun" } }
    }).ConfigureAwait(false);

    var entry = JsonDocument.Parse(await save.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("entries").EnumerateArray().Single();

    var effective = entry.GetProperty("effectiveParameters").EnumerateArray()
        .Single(e => e.GetProperty("name").GetString() == "adbSerial");
    effective.GetProperty("value").GetString().Should().Be("emulator-5558");
    effective.GetProperty("originLayer").GetString().Should().Be("default");
  }

  [Fact]
  public async Task TwoEntriesOnOneSequenceKeepIndependentValues() {
    // FR-012: this is what lets several entries share a sequence and differ only by a value.
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client, "tpl-two-entries-seq").ConfigureAwait(false);

    var save = await client.PostAsJsonAsync("/api/queue-templates", new {
      name = "Two Entry Template",
      overwrite = true,
      entries = new object[] {
        new { sequenceId, scheduleType = "OncePerRun", parameterValues = new object[] { new { name = "slot", value = "one" } } },
        new { sequenceId, scheduleType = "OncePerRun", parameterValues = new object[] { new { name = "slot", value = "two" } } }
      }
    }).ConfigureAwait(false);

    var entries = JsonDocument.Parse(await save.Content.ReadAsStringAsync().ConfigureAwait(false))
        .RootElement.GetProperty("entries").EnumerateArray().ToList();

    entries.Should().HaveCount(2);
    entries[0].GetProperty("parameterValues").EnumerateArray().Single()
        .GetProperty("value").GetString().Should().Be("one");
    entries[1].GetProperty("parameterValues").EnumerateArray().Single()
        .GetProperty("value").GetString().Should().Be("two");
  }

  [Fact]
  public async Task AReservedValueNameIsRejected() {
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client, "tpl-reserved-seq").ConfigureAwait(false);

    var save = await client.PostAsJsonAsync("/api/queue-templates", new {
      name = "Reserved Template",
      overwrite = true,
      entries = new object[] {
        new {
          sequenceId,
          scheduleType = "OncePerRun",
          parameterValues = new object[] { new { name = "iteration", value = "3" } }
        }
      }
    }).ConfigureAwait(false);

    save.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var root = JsonDocument.Parse(await save.Content.ReadAsStringAsync().ConfigureAwait(false)).RootElement;
    root.GetProperty("error").GetString().Should().Be("invalid_parameter_value_name");
  }

  [Fact]
  public async Task AnAdHocValueNameIsAcceptedEvenThoughTheSequenceDeclaresNothing() {
    // FR-012a: an ad-hoc name reaches a command at any depth, so it must not be rejected here.
    using var app = CreateFactory();
    var client = AuthedClient(app);
    var sequenceId = await CreateSequenceAsync(client, "tpl-adhoc-seq").ConfigureAwait(false);

    var save = await client.PostAsJsonAsync("/api/queue-templates", new {
      name = "AdHoc Template",
      overwrite = true,
      entries = new object[] {
        new {
          sequenceId,
          scheduleType = "OncePerRun",
          parameterValues = new object[] { new { name = "adbSerial", value = "emulator-5560" } }
        }
      }
    }).ConfigureAwait(false);

    ShouldBeSaved(save);
  }
}
