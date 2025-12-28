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
    var expectedTags = new[] { "Actions", "Sequences", "Sessions", "Configuration", "Triggers", "Images" };
    allTags.Should().Contain(expectedTags);

    AssertRequestExample(paths, "/api/actions", "post");
    AssertResponseExample(paths, "/api/actions", "get", "200");
    AssertResponseExample(paths, "/api/actions", "post", "201");

    AssertRequestExample(paths, "/api/sequences", "post");
    AssertResponseExample(paths, "/api/sequences", "get", "200");
    AssertResponseExample(paths, "/api/sequences", "post", "201");

    AssertRequestExample(paths, "/api/sessions", "post");
    AssertResponseExample(paths, "/api/sessions", "post", "201");
    AssertRequestExample(paths, "/api/sessions/{id}/inputs", "post");
    AssertResponseExample(paths, "/api/sessions/{id}/inputs", "post", "202");

    AssertResponseExample(paths, "/api/config", "get", "200");
    AssertResponseExample(paths, "/api/config/logging", "get", "200");

    AssertRequestExample(paths, "/api/triggers", "post");
    AssertResponseExample(paths, "/api/triggers", "get", "200");

    AssertRequestExample(paths, "/api/images/detect", "post");
    AssertResponseExample(paths, "/api/images/detect", "post", "200");
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

  private static void AssertRequestExample(JsonElement paths, string route, string method)
  {
    var op = GetOperation(paths, route, method);
    op.TryGetProperty("requestBody", out var body).Should().BeTrue($"Request body missing for {method.ToUpperInvariant()} {route}");
    body.TryGetProperty("content", out var content).Should().BeTrue($"Content missing for {method.ToUpperInvariant()} {route}");
    content.TryGetProperty("application/json", out var jsonContent).Should().BeTrue($"application/json missing for {method.ToUpperInvariant()} {route}");
    jsonContent.TryGetProperty("example", out var example).Should().BeTrue($"Example missing for {method.ToUpperInvariant()} {route}");
    example.ValueKind.Should().NotBe(JsonValueKind.Null);
  }

  private static void AssertResponseExample(JsonElement paths, string route, string method, string status)
  {
    var op = GetOperation(paths, route, method);
    op.TryGetProperty("responses", out var responses).Should().BeTrue($"Responses missing for {method.ToUpperInvariant()} {route}");
    responses.TryGetProperty(status, out var resp).Should().BeTrue($"Status {status} missing for {method.ToUpperInvariant()} {route}");
    if (resp.TryGetProperty("content", out var content) && content.TryGetProperty("application/json", out var jsonContent))
    {
      jsonContent.TryGetProperty("example", out var example).Should().BeTrue($"Example missing for {method.ToUpperInvariant()} {route} {status}");
      example.ValueKind.Should().NotBe(JsonValueKind.Null);
    }
  }

  private static JsonElement GetOperation(JsonElement paths, string route, string method)
  {
    paths.TryGetProperty(route, out var path).Should().BeTrue($"Path {route} missing from swagger");
    path.TryGetProperty(method, out var op).Should().BeTrue($"Method {method} missing for {route}");
    return op;
  }
}
