using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.UnitTests.Gamepacks;
using Xunit;
using F = GameBot.UnitTests.Gamepacks.GamepackStepFixtures;

#pragma warning disable CA2007, CA1861, CA1707, CA2000, CA1305, CA1307, CA1310

namespace GameBot.ContractTests.Gamepacks;

/// <summary>Feature 151: an installed Action step keeps its command action, and a step problem blocks the install.</summary>
public sealed class GamepackActionStepInstallContractTests {
  internal static async Task<JsonElement> SequenceByNameAsync(GamepackContractHost host, string name) {
    var list = await host.GetJsonAsync("/api/sequences");
    var summary = list.EnumerateArray().First(s => s.GetProperty("name").GetString() == name);
    return await host.GetJsonAsync("/api/sequences/" + summary.GetProperty("id").GetString());
  }

  private static void ExpectCommandAction(JsonElement step, string commandId, string? commandName) {
    var primitive = step.GetProperty("primitiveAction");
    primitive.ValueKind.Should().Be(JsonValueKind.Object);
    primitive.GetProperty("type").GetString().Should().Be("command");
    primitive.GetProperty("payload").GetProperty("commandId").GetString().Should().Be(commandId);
    var reference = step.GetProperty("commandReference");
    reference.GetProperty("commandId").GetString().Should().Be(commandId);
    if (commandName is not null) {
      reference.GetProperty("commandName").GetString().Should().Be(commandName);
    }
  }

  [Fact]
  public async Task TheIssue321FileKeepsTheCommandAction() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(F.Issue321Pack());

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var sequence = await SequenceByNameAsync(host, F.SequenceName);
    var step = sequence.GetProperty("steps").EnumerateArray().Single();
    step.GetProperty("stepId").GetString().Should().Be(F.StepId);
    ExpectCommandAction(step, F.CommandId, F.CommandName);
    step.GetProperty("commandReference").GetProperty("isResolved").GetBoolean().Should().BeTrue();
    var commands = await host.GetJsonAsync("/api/commands");
    commands.EnumerateArray().Should().Contain(c => c.GetProperty("id").GetString() == F.CommandId);
  }

  [Fact]
  public async Task EachFormKeepsTheCommandAction() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(F.PackWith(F.PrimitiveOnly("a"), F.ReferenceOnly("b"), F.CommandIdOnly("c"), F.InLoop(F.CommandIdAndPrimitive("d"))));

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var steps = (await SequenceByNameAsync(host, F.SequenceName)).GetProperty("steps").EnumerateArray().ToArray();
    ExpectCommandAction(steps[0], F.CommandId, null);
    ExpectCommandAction(steps[1], F.CommandId, F.CommandName);
    ExpectCommandAction(steps[2], F.CommandId, null);
    ExpectCommandAction(steps[3].GetProperty("body").EnumerateArray().Single(), F.CommandId, null);
  }

  [Theory]
  [InlineData("ReferenceVersusPrimitive")]
  [InlineData("ActionVersusPrimitiveType")]
  public async Task AConflictIsAStructureProblemInTheDryRun(string form) {
    using var host = new GamepackContractHost();
    var step = typeof(F).GetMethod(form)!.Invoke(null, null)!.ToString()!;

    var response = await host.InstallAsync(F.PackWith(step), dryRun: true);

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    var problem = body.GetProperty("problems").EnumerateArray().Single();
    problem.GetProperty("category").GetString().Should().Be("structure");
    problem.GetProperty("name").GetString().Should().Be(F.SequenceName);
    problem.GetProperty("message").GetString().Should().Contain("'s1'");
  }

  [Theory]
  [InlineData("UnresolvablePrimitive")]
  [InlineData("UnresolvableReference")]
  [InlineData("UnresolvableCommandId")]
  public async Task AMissingCommandIsAReferenceProblemInTheDryRun(string form) {
    using var host = new GamepackContractHost();
    var step = typeof(F).GetMethod(form)!.Invoke(null, null)!.ToString()!;

    var response = await host.InstallAsync(F.PackWith(step), dryRun: true);

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    body.GetProperty("problems").EnumerateArray().Should().ContainSingle(p => p.GetProperty("category").GetString() == "reference");
  }

  [Fact]
  public async Task AnAliasOnALoopStepIsAStructureProblemInTheDryRun() {
    using var host = new GamepackContractHost();

    var response = await host.InstallAsync(F.PackWith(F.LoopWith(F.PrimitiveAlias)), dryRun: true);

    var body = await GamepackContractHost.ReadAsync(response);
    body.GetProperty("valid").GetBoolean().Should().BeFalse();
    var problem = body.GetProperty("problems").EnumerateArray().Single();
    problem.GetProperty("category").GetString().Should().Be("structure");
    problem.GetProperty("message").GetString().Should().Contain("'primitiveAction'").And.Contain("'L1'");
  }

  [Fact]
  public async Task AnUnknownMemberBlocksTheDryRunAndTheInstall() {
    using var host = new GamepackContractHost();
    var pack = F.PackWith(F.UnknownMember());

    var dry = await GamepackContractHost.ReadAsync(await host.InstallAsync(pack, dryRun: true));
    dry.GetProperty("valid").GetBoolean().Should().BeFalse();
    var problem = dry.GetProperty("problems").EnumerateArray().Single();
    problem.GetProperty("category").GetString().Should().Be("structure");
    problem.GetProperty("message").GetString().Should().Contain("'mystery'").And.Contain("'s1'");

    var real = await host.InstallAsync(pack);
    real.StatusCode.Should().NotBe(HttpStatusCode.OK);
    var body = await GamepackContractHost.ReadAsync(real);
    body.GetProperty("problems").EnumerateArray().Should().ContainSingle(p => p.GetProperty("message").GetString()!.Contains("'mystery'"));
    (await host.GetJsonAsync("/api/sequences")).GetArrayLength().Should().Be(0);
  }
}
