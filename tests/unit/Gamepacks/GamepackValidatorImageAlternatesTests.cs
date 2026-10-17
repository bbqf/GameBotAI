using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861, CA1416

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 153: the dry run applies the alternates rules of the image API.</summary>
public sealed class GamepackValidatorImageAlternatesTests {
  private static string Pack(string version, params object[] images) => GamepackTestKit.Pack("p", version, images: images);

  private static object Img(string id, params string[] list) => GamepackTestKit.ImgAlt(id, list);

  private static async Task<GamepackProblem> OneProblemAsync(GamepackTestKit kit, string text) {
    var result = await kit.CheckAsync(text);
    var problem = result.Problems.Should().ContainSingle().Which;
    problem.Category.Should().Be(GamepackProblemCategories.Structure);
    problem.ObjectType.Should().Be(GamepackObjectTypes.Image);
    return problem;
  }

  [Fact]
  public async Task ASelfReferenceIsAnError() {
    using var kit = new GamepackTestKit();

    var problem = await OneProblemAsync(kit, Pack("1", Img("a", "a")));

    problem.Name.Should().Be("a");
    problem.Message.Should().StartWith("Image 'a': An image cannot be its own alternate. Ids: a. ");
  }

  [Fact]
  public async Task AnUnknownIdIsAnError() {
    using var kit = new GamepackTestKit();

    var problem = await OneProblemAsync(kit, Pack("1", Img("a", "nope")));

    problem.Message.Should().Contain("Ids: nope.");
  }

  [Fact]
  public async Task ADuplicateIdIsAnError() {
    using var kit = new GamepackTestKit();

    var problem = await OneProblemAsync(kit, Pack("1", Img("a", "b", "B"), Img("b")));

    problem.Message.Should().Contain("must not repeat");
  }

  [Fact]
  public async Task MoreThanEightIdsIsAnErrorWithoutAnIdsPart() {
    using var kit = new GamepackTestKit();
    var others = Enumerable.Range(0, 9).Select(i => "o" + i).ToArray();
    var images = others.Select(o => Img(o)).Concat(new[] { Img("a", others) }).ToArray();

    var problem = await OneProblemAsync(kit, Pack("1", images));

    problem.Message.Should().Contain("at most 8").And.NotContain("Ids:");
  }

  [Fact]
  public async Task ABadIdFormIsAnError() {
    using var kit = new GamepackTestKit();

    var problem = await OneProblemAsync(kit, Pack("1", Img("a", "bad id!")));

    problem.Message.Should().Contain("Ids: bad id!.");
  }

  [Fact]
  public async Task AStoredImageThatTheInstallRemovesIsUnknown() {
    using var kit = new GamepackTestKit();
    (await kit.Service.InstallAsync(Pack("1", Img("a"), Img("b")), false)).Status.Should().Be(GamepackInstallStatus.Ok);

    var problem = await OneProblemAsync(kit, Pack("2", Img("c", "b")));

    problem.Message.Should().Contain("Ids: b.");
  }

  [Fact]
  public async Task AStoredImageThatStaysIsValid() {
    using var kit = new GamepackTestKit();
    await kit.AddUserObjectsAsync(Pack("0", GamepackTestKit.Img("user")));

    var result = await kit.CheckAsync(Pack("1", Img("a", "user")));

    result.IsValid.Should().BeTrue();
  }

  [Fact]
  public async Task AnEmptyListIsValid() {
    using var kit = new GamepackTestKit();

    (await kit.CheckAsync(Pack("1", Img("a")))).IsValid.Should().BeTrue();
  }
}
