#pragma warning disable CA1861 // Test data arrays are small and used one time.
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Parameters;
using GameBot.Domain.QueueTemplates;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>Feature 179: the stores keep list values, and old files without lists still load.</summary>
public sealed class ParameterListPersistenceTests {
  private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

  [Fact]
  public void OldEntryFileWithoutValuesLoadsAndHasNoList() {
    const string json = """
      { "sequenceId": "s1", "enabled": true, "scheduleType": "OncePerRun",
        "parameterValues": [ { "name": "quests", "value": "a" } ] }
      """;

    var entry = JsonSerializer.Deserialize<QueueTemplateEntry>(json, WebOptions);

    var binding = entry!.ParameterValues.Should().ContainSingle().Subject;
    binding.Value.Should().Be("a");
    binding.Values.Should().BeNull();
  }

  [Fact]
  public void NewEntryFileWithValuesLoadsAndSavesTheList() {
    const string json = """
      { "sequenceId": "s1", "enabled": true, "scheduleType": "OncePerRun",
        "parameterValues": [ { "name": "quests", "value": "b|a", "values": ["b", "a"] } ] }
      """;

    var entry = JsonSerializer.Deserialize<QueueTemplateEntry>(json, WebOptions);
    entry!.ParameterValues.Single().Values.Should().Equal("b", "a");

    var saved = JsonSerializer.Serialize(entry, WebOptions);
    var reloaded = JsonSerializer.Deserialize<QueueTemplateEntry>(saved, WebOptions);
    var binding = reloaded!.ParameterValues.Single();
    binding.Values.Should().Equal("b", "a");
    binding.Value.Should().Be("b|a");
  }

  [Fact]
  public void BindingWithoutAListOmitsTheValuesMember() {
    var json = JsonSerializer.Serialize(new ParameterBinding { Name = "x", Value = "1" }, WebOptions);
    json.Should().NotContain("values");
  }

  [Fact]
  public void DeclarationWithoutTheFlagOmitsTheNewMembers() {
    var json = JsonSerializer.Serialize(new ParameterDeclaration { Name = "x" }, WebOptions);
    json.Should().NotContain("multiple").And.NotContain("minItems").And.NotContain("maxItems");
  }

  [Fact]
  public void MultiValueDeclarationRoundTripsThroughJson() {
    var declaration = new ParameterDeclaration {
      Name = "q", Kind = ParameterInputKind.Image, AllowedImages = new[] { "a", "b" },
      Multiple = true, MinItems = 1, MaxItems = 2
    };

    var loaded = JsonSerializer.Deserialize<ParameterDeclaration>(JsonSerializer.Serialize(declaration, WebOptions), WebOptions);

    loaded!.Multiple.Should().BeTrue();
    loaded.MinItems.Should().Be(1);
    loaded.MaxItems.Should().Be(2);
  }
}
