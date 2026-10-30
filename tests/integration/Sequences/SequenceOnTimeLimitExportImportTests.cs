using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 170: the option travels with the stored JSON. The member is a normal property, so an export
/// and an import (the stored file, and a copy made from the read response) keep it.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SequenceOnTimeLimitExportImportTests : OnTimeLimitIntegrationTestBase {
  [Fact]
  public async Task TheStoredJsonKeepsTheOptionThroughExportAndImport() {
    var root = Path.Combine(Path.GetTempPath(), $"gamebot-onlimit-{Guid.NewGuid():N}");
    try {
      var source = new FileSequenceRepository(root);
      await source.CreateAsync(new CommandSequence { Id = "s1", Name = "s1", OnTimeLimit = "end" }).ConfigureAwait(false);
      var exported = await File.ReadAllTextAsync(Path.Combine(root, "commands", "sequences", "s1.json")).ConfigureAwait(false);

      var imported = JsonSerializer.Deserialize<CommandSequence>(exported)!;
      imported.OnTimeLimit.Should().Be("end");

      var target = new FileSequenceRepository(Path.Combine(root, "target"));
      imported.Id = "s2";
      await target.CreateAsync(imported).ConfigureAwait(false);
      (await target.GetAsync("s2").ConfigureAwait(false))!.OnTimeLimit.Should().Be("end");
    }
    finally {
      try { Directory.Delete(root, recursive: true); }
      catch (IOException) { /* best effort */ }
    }
  }

  [Fact]
  public async Task ACopyMadeFromTheReadResponseKeepsTheOption() {
    using var app = new WebApplicationFactory<Program>();
    using var client = CreateClient(app);
    var body = new Dictionary<string, object?> {
      ["name"] = "OnLimit-Export-" + Guid.NewGuid().ToString("N"),
      ["version"] = 1,
      ["onTimeLimit"] = "end",
      ["steps"] = new object[] {
        new { stepId = "t1", label = "Tap", stepType = "Action", primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 1 } } }
      }
    };
    var created = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), body).ConfigureAwait(false);
    created.StatusCode.Should().Be(HttpStatusCode.Created);
    var source = await created.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

    var copy = new Dictionary<string, object?> {
      ["name"] = "OnLimit-Export-Copy-" + Guid.NewGuid().ToString("N"),
      ["version"] = 1,
      ["steps"] = source.GetProperty("steps"),
      ["onTimeLimit"] = source.GetProperty("onTimeLimit").GetString()
    };
    var copied = await client.PostAsJsonAsync(new Uri("/api/sequences", UriKind.Relative), copy).ConfigureAwait(false);
    copied.StatusCode.Should().Be(HttpStatusCode.Created);

    (await copied.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false)).GetProperty("onTimeLimit").GetString().Should().Be("end");
  }
}
