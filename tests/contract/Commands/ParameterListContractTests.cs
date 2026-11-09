#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace GameBot.ContractTests.Commands;

/// <summary>Feature 179: multi-value declarations on the command API.</summary>
public sealed class ParameterListContractTests {
  private static WebApplicationFactory<Program> CreateFactory() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_DYNAMIC_PORT", "true");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    return new WebApplicationFactory<Program>();
  }

  private static async Task<(HttpStatusCode Status, JsonElement Body)> PostCommandAsync(
      WebApplicationFactory<Program> app, object declaration) {
    var client = app.CreateClient();
    client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
    var response = await client.PostAsJsonAsync("/api/commands", new {
      name = $"list-{Guid.NewGuid():N}",
      parameters = new[] { declaration },
      steps = new object[] {
        new {
          type = "PrimitiveTap",
          order = 0,
          primitiveTap = new { detectionTarget = new { referenceImageId = "exchange-button", confidence = 0.9 } }
        }
      }
    }).ConfigureAwait(false);
    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
  }

  [Fact]
  public async Task MultiValueDeclarationRoundTripsOnACommand() {
    using var app = CreateFactory();

    var (status, body) = await PostCommandAsync(app, new {
      name = "quests", type = "text", kind = "image", multiple = true, minItems = 1,
      allowedImages = new[] { "q-a", "q-b" }
    }).ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.Created, body.ToString());
    var declaration = body.GetProperty("parameters").EnumerateArray().Single();
    declaration.GetProperty("multiple").GetBoolean().Should().BeTrue();
    declaration.GetProperty("minItems").GetInt32().Should().Be(1);
  }

  [Fact]
  public async Task InvalidMultiValueDeclarationIsRefusedOnACommand() {
    using var app = CreateFactory();

    var (status, body) = await PostCommandAsync(app, new { name = "n", type = "number", kind = "number", multiple = true })
        .ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.BadRequest);
    body.GetProperty("error").GetString().Should().Be("invalid_parameter_declaration");
  }

  [Fact]
  public async Task DeclarationWithoutTheFlagGivesTheSameShape() {
    using var app = CreateFactory();

    var (status, body) = await PostCommandAsync(app, new {
      name = "pic", type = "text", kind = "image", allowedImages = new[] { "q-a" }
    }).ConfigureAwait(false);

    status.Should().Be(HttpStatusCode.Created, body.ToString());
    body.GetProperty("parameters").EnumerateArray().Single().TryGetProperty("multiple", out _).Should().BeFalse();
  }
}
