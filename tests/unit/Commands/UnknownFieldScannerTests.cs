using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using GameBot.Service.Endpoints;
using Xunit;

namespace GameBot.UnitTests.Commands;

public class UnknownFieldScannerTests {
  internal sealed class Leaf {
    public int? Value { get; init; }
    [JsonIgnore]
    public bool Hidden { get; init; }
  }

  internal sealed class Item {
    public string? Name { get; init; }
    public Leaf? Leaf { get; init; }
    public Dictionary<string, string>? Templates { get; init; }
  }

  internal sealed class Root {
    public string? Title { get; init; }
    public Collection<Item>? Items { get; init; }
    public Item? Single { get; init; }
  }

  private static IReadOnlyList<string> Scan(string json) {
    using var doc = JsonDocument.Parse(json);
    return UnknownFieldScanner.Find(doc.RootElement, typeof(Root));
  }

  [Fact]
  public void UnknownField_InArrayItem_ReportsIndexPath() {
    Scan("""{"items":[{"name":"a"},{"bogus":1}]}""").Should().Equal("items[1].bogus");
  }

  [Fact]
  public void UnknownField_Nested_ReportsFullPath() {
    Scan("""{"single":{"leaf":{"bogus":1}}}""").Should().Equal("single.leaf.bogus");
  }

  [Fact]
  public void DifferentLetterCase_IsNotUnknown() {
    Scan("""{"TITLE":"x","Items":[{"NAME":"a","LEAF":{"VALUE":1}}]}""").Should().BeEmpty();
  }

  [Fact]
  public void JsonIgnoreProperty_CountsAsUnknown() {
    Scan("""{"single":{"leaf":{"hidden":true}}}""").Should().Equal("single.leaf.hidden");
  }

  [Fact]
  public void DictionaryKeys_AreNotFlagged() {
    Scan("""{"single":{"templates":{"any.key":"{{x}}","other":"y"}}}""").Should().BeEmpty();
  }

  [Fact]
  public void NullValue_ForUnknownName_IsUnknown() {
    Scan("""{"bogus":null}""").Should().Equal("bogus");
  }

  [Fact]
  public void AllUnknownPaths_AreReturnedInOrder() {
    Scan("""{"first":1,"items":[{"second":2}],"single":{"third":3}}""")
      .Should().Equal("first", "items[0].second", "single.third");
  }

  [Fact]
  public void OnlyKnownFields_ReturnEmptyList() {
    Scan("""{"title":"t","items":[{"name":"a","leaf":{"value":2}}],"single":{"name":"b"}}""").Should().BeEmpty();
  }
}
