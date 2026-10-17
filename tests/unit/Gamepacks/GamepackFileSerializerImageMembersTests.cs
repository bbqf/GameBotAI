using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 153: the parse reports an unknown image member and a wrong-shape alternates list.</summary>
public sealed class GamepackFileSerializerImageMembersTests {
  private const string Png = GamepackTestKit.PngBase64;

  private static string Pack(string imagesJson) =>
    "{\"manifest\":{\"name\":\"p\",\"version\":\"1\"},\"sequences\":[],\"commands\":[],\"images\":" + imagesJson + "}";

  [Fact]
  public void AnUnknownMemberGivesAnImageProblemThatNamesTheImageAndTheMember() {
    var result = GamepackFileSerializer.Parse(Pack("[{\"id\":\"a\",\"contentType\":\"image/png\",\"dataBase64\":\"" + Png + "\",\"bogusMember\":1}]"));

    var problem = result.Problems.Should().ContainSingle().Which;
    problem.Category.Should().Be(GamepackProblemCategories.Structure);
    problem.ObjectType.Should().Be(GamepackObjectTypes.Image);
    problem.Name.Should().Be("a");
    problem.Message.Should().Contain("bogusMember").And.Contain("'a'");
  }

  [Fact]
  public void KnownMembersInAnyCaseAreAccepted() {
    var result = GamepackFileSerializer.Parse(Pack("[{\"ID\":\"a\",\"ContentType\":\"image/png\",\"FileName\":\"a.png\",\"DataBase64\":\"" + Png + "\",\"Alternates\":[\"b\"]}]"));

    result.Problems.Should().BeEmpty();
    result.File!.Images.Single().Alternates.Should().Equal("b");
  }

  [Fact]
  public void AnEntryWithoutAnIdUsesThePositionName() {
    var result = GamepackFileSerializer.Parse(Pack("[{\"contentType\":\"image/png\",\"extra\":true}]"));

    result.Problems.Should().ContainSingle().Which.Name.Should().Be("images[0]");
  }

  [Fact]
  public void AnEntryThatIsNotAnObjectGivesNoMemberProblem() {
    var result = GamepackFileSerializer.Parse(Pack("[5]"));

    result.Problems.Should().NotContain(p => p.Message.Contains("unknown member"));
  }

  [Theory]
  [InlineData("\"alternates\":\"b\"")]
  [InlineData("\"alternates\":[1]")]
  public void AWrongShapeListNamesTheImage(string member) {
    var result = GamepackFileSerializer.Parse(Pack("[{\"id\":\"a\",\"contentType\":\"image/png\",\"dataBase64\":\"" + Png + "\"," + member + "}]"));

    result.Problems.Should().ContainSingle().Which.Name.Should().Be("a");
  }
}
