using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;
using static GameBot.ContractTests.Sequences.FriendlyContractHelpers;

namespace GameBot.ContractTests.Sequences;

/// <summary>Feature 168 (FR-018): a declaration without friendly members gets a response without them.</summary>
public sealed class FriendlyBackCompatContractTests {
  private static readonly string[] NewMembers = {
    "label", "helpText", "kind", "options", "allowedImages", "min", "max", "unit", "pattern"
  };

  [Fact]
  public async Task PlainSequenceDeclarationResponseHasNoNewMembers() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var (response, body) = await PostSequenceAsync(client, new { name = "plain", type = "text", @default = "x" })
        .ConfigureAwait(false);

    response.StatusCode.Should().Be(HttpStatusCode.Created);
    var declaration = body.GetProperty("parameters")[0];
    foreach (var member in NewMembers) {
      declaration.TryGetProperty(member, out _).Should().BeFalse($"'{member}' must be omitted when empty");
    }
  }

  [Fact]
  public async Task PlainCommandDeclarationResponseHasNoNewMembers() {
    using var app = CreateFactory();
    var client = AuthedClient(app);

    var response = await client.PostAsJsonAsync("/api/commands", new {
      name = "plain-cmd",
      parameters = new object[] { new { name = "n", type = "number", @default = "3" } },
      steps = new object[] { new { type = "KeyInput", order = 0, keyInput = new { key = "KEYCODE_HOME" } } }
    }).ConfigureAwait(false);

    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    response.StatusCode.Should().Be(HttpStatusCode.Created, text);
    foreach (var member in NewMembers) {
      text.Should().NotContain($"\"{member}\"", $"'{member}' must be omitted when empty");
    }
  }
}
