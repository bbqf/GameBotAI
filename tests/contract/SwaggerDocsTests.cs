using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests;

public sealed class SwaggerDocsTests : IDisposable
{
  private readonly string? _prevUseAdb;
  private readonly string? _prevDynPort;
  private readonly string? _prevToken;

  public SwaggerDocsTests()
  {
    _prevUseAdb = Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB");
    _prevDynPort = Environment.GetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT");
    _prevToken = Environment.GetEnvironmentVariable("GAMEBOT_AUTH_TOKEN");
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
  }

  public void Dispose()
  {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", _prevUseAdb);
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", _prevDynPort);
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", _prevToken);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task SwaggerDocumentsHaveTagsAndExamples()
  {
    using var app = new WebApplicationFactory<Program>();
    var client = app.CreateClient();

    var resp = await client.GetAsync(new Uri("/swagger/v1/swagger.json", UriKind.Relative)).ConfigureAwait(false);
    resp.EnsureSuccessStatusCode();

    var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
    using var doc = JsonDocument.Parse(json);
    var root = doc.RootElement;
    var paths = root.GetProperty("paths");

    var allTags = CollectTags(paths);
    var expectedTags = new[] { "Actions", "Commands", "Games", "Sequences", "Sessions", "Configuration", "Triggers", "Images" };
    allTags.Should().Contain(expectedTags);

    foreach (var path in paths.EnumerateObject())
    {
      foreach (var method in path.Value.EnumerateObject())
      {
        var op = method.Value;
        var route = path.Name;
        var verb = method.Name;

        AssertRequestHasExampleAndSchema(op, route, verb);
        AssertResponsesHaveExamplesAndSchemas(op, route, verb);
      }
    }
  }

  private static HashSet<string> CollectTags(JsonElement paths)
  {
    var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in paths.EnumerateObject())
    {
      foreach (var method in path.Value.EnumerateObject())
      {
        if (method.Value.TryGetProperty("tags", out var tagArray) && tagArray.ValueKind == JsonValueKind.Array)
        {
          foreach (var tag in tagArray.EnumerateArray())
          {
            if (tag.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tag.GetString()))
            {
              tags.Add(tag.GetString()!);
            }
          }
        }
      }
    }
    return tags;
  }

  private static void AssertRequestHasExampleAndSchema(JsonElement operation, string route, string method)
  {
    if (!operation.TryGetProperty("requestBody", out var body))
    {
      return; // endpoints without bodies are acceptable
    }

    body.TryGetProperty("content", out var content).Should().BeTrue($"Content missing for {method.ToUpperInvariant()} {route}");
    content.TryGetProperty("application/json", out var jsonContent).Should().BeTrue($"application/json missing for {method.ToUpperInvariant()} {route}");
    jsonContent.TryGetProperty("example", out var example).Should().BeTrue($"Example missing for {method.ToUpperInvariant()} {route}");
    example.ValueKind.Should().NotBe(JsonValueKind.Null);
    jsonContent.TryGetProperty("schema", out var schema).Should().BeTrue($"Schema missing for {method.ToUpperInvariant()} {route}");
    schema.ValueKind.Should().NotBe(JsonValueKind.Null);
  }

  private static void AssertResponsesHaveExamplesAndSchemas(JsonElement operation, string route, string method)
  {
    operation.TryGetProperty("responses", out var responses).Should().BeTrue($"Responses missing for {method.ToUpperInvariant()} {route}");
    foreach (var status in responses.EnumerateObject())
    {
      var statusCode = status.Name;
      var resp = status.Value;
      if (resp.TryGetProperty("content", out var content) && content.TryGetProperty("application/json", out var jsonContent))
      {
        jsonContent.TryGetProperty("example", out var example).Should().BeTrue($"Example missing for {method.ToUpperInvariant()} {route} {statusCode}");
        example.ValueKind.Should().NotBe(JsonValueKind.Null);
        jsonContent.TryGetProperty("schema", out var schema).Should().BeTrue($"Schema missing for {method.ToUpperInvariant()} {route} {statusCode}");
        schema.ValueKind.Should().NotBe(JsonValueKind.Null);
      }
    }
  }
}
