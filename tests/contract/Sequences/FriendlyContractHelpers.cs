using System;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GameBot.ContractTests.Sequences;

/// <summary>Shared helpers for the feature 168 contract tests.</summary>
internal static class FriendlyContractHelpers {
  public static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  public static System.Net.Http.HttpClient AuthedClient(WebApplicationFactory<Program> app) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    return client;
  }

  public static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

  /// <summary>Posts a sequence with one tap step and the given declarations.</summary>
  public static async Task<(System.Net.Http.HttpResponseMessage Response, JsonElement Body)> PostSequenceAsync(
      System.Net.Http.HttpClient client, params object[] parameters) {
    var response = await client.PostAsJsonAsync("/api/sequences", new {
      name = Unique("friendly"),
      version = 1,
      parameters,
      steps = new object[] {
        new { stepId = "s1", stepType = "Action", primitiveAction = new { type = "tap", schemaVersion = "v1", payload = new { x = 1, y = 2 } } }
      }
    }).ConfigureAwait(false);
    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    return (response, JsonDocument.Parse(text).RootElement);
  }

  public static bool HasWarning(JsonElement body, string code) {
    if (!body.TryGetProperty("warnings", out var warnings) || warnings.ValueKind != JsonValueKind.Array) return false;
    foreach (var w in warnings.EnumerateArray()) {
      if (w.GetProperty("code").GetString() == code) return true;
    }

    return false;
  }
}
