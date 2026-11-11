using FluentAssertions;
using GameBot.Service.Security;
using GameBot.UnitTests.Queues;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameBot.UnitTests.Security;

public sealed class AuthCommandTests : IDisposable {
  private readonly string _root = Path.Combine(Path.GetTempPath(), "gamebot-cmd-" + Guid.NewGuid().ToString("N"));
  private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero));

  public void Dispose() {
    if (Directory.Exists(_root)) {
      Directory.Delete(_root, recursive: true);
    }
  }

  private static IConfiguration Config(bool enabled, string? origin) =>
    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
      ["Service:Auth:Enabled"] = enabled ? "true" : "false",
      ["Service:Auth:PublicOrigin"] = origin
    }).Build();

  private (int Code, string Out, string Err) Run(string[] args, IConfiguration? config = null) {
    using var output = new StringWriter();
    using var error = new StringWriter();
    var full = args.Concat(new[] { "--data-dir", _root }).ToArray();
    var code = AuthCommand.Run(full, output, error, config ?? Config(true, "https://bot.example.com"), _clock);
    return (code, output.ToString(), error.ToString());
  }

  [Fact]
  public void IsCommandDetectsTheFirstArgument() {
    AuthCommand.IsCommand(new[] { "auth", "list" }).Should().BeTrue();
    AuthCommand.IsCommand(new[] { "--urls", "x" }).Should().BeFalse();
    AuthCommand.IsCommand(Array.Empty<string>()).Should().BeFalse();
  }

  [Fact]
  public void NewCodePrintsTheCodeAndTheRegistrationLink() {
    var (code, output, _) = Run(new[] { "auth", "new-code" });

    code.Should().Be(0);
    output.Should().MatchRegex("Registration code: [A-Z0-9]{4}-[A-Z0-9]{4}");
    output.Should().Contain("valid for 10 minutes").And.Contain("https://bot.example.com/register");
    output.Should().NotContain("Warning");
    File.Exists(Path.Combine(_root, "auth", "registration-code.json")).Should().BeTrue();
  }

  [Fact]
  public void NewCodeWarnsWhenTheFunctionIsOffAndTellsAboutAMissingOrigin() {
    var (code, output, _) = Run(new[] { "auth", "new-code" }, Config(false, null));

    code.Should().Be(0);
    output.Should().Contain("Warning: the sign-in function is off. Set GAMEBOT_AUTH_ENABLED=true.");
    output.Should().Contain("Set GAMEBOT_AUTH_PUBLIC_ORIGIN first.");
  }

  [Fact]
  public void ListShowsPasskeysAndSessions() {
    var passkeys = new PasskeyStore(Path.Combine(_root, "auth"), _clock);
    passkeys.Add("cred-1", "key", 0, "My phone", out var added);
    new SessionStore(Path.Combine(_root, "auth"), passkeys, _clock).Create(added!.Id);

    var (code, output, _) = Run(new[] { "auth", "list" });

    code.Should().Be(0);
    output.Should().Contain(added.Id).And.Contain("My phone").And.Contain("Active sessions: 1");
    output.Should().NotContain("key");
  }

  [Fact]
  public void RemoveEndsTheSessionsOfThePasskeyAndWritesARecord() {
    var dir = Path.Combine(_root, "auth");
    var passkeys = new PasskeyStore(dir, _clock);
    passkeys.Add("cred-1", "key", 0, "My phone", out var added);
    var sessions = new SessionStore(dir, passkeys, _clock);
    var token = sessions.Create(added!.Id);

    var (code, _, _) = Run(new[] { "auth", "remove", added.Id });

    code.Should().Be(0);
    new SessionStore(dir, new PasskeyStore(dir, _clock), _clock).Validate(token).Should().BeNull();
    new PasskeyStore(dir, _clock).List().Should().BeEmpty();
    File.ReadAllText(Path.Combine(dir, "sign-in-records.jsonl")).Should().Contain("passkey_removed").And.Contain("local-command");
  }

  [Fact]
  public void RemoveOfAnUnknownIdGivesExitCodeTwo() {
    var (code, _, error) = Run(new[] { "auth", "remove", "nope" });

    code.Should().Be(2);
    error.Should().Contain("nope");
  }

  [Fact]
  public void EndSessionsEndsAll() {
    var dir = Path.Combine(_root, "auth");
    var passkeys = new PasskeyStore(dir, _clock);
    passkeys.Add("cred-1", "key", 0, "x", out var added);
    var sessions = new SessionStore(dir, passkeys, _clock);
    var token = sessions.Create(added!.Id);

    var (code, _, _) = Run(new[] { "auth", "end-sessions" });

    code.Should().Be(0);
    new SessionStore(dir, passkeys, _clock).Validate(token).Should().BeNull();
  }

  [Theory]
  [InlineData("auth")]
  [InlineData("auth --help")]
  public void NoCommandOrHelpPrintsUsage(string line) {
    var args = line.Split(' ');

    var (code, output, _) = Run(args);

    code.Should().Be(0);
    output.Should().Contain("Usage: GameBot.Service.exe auth");
  }

  [Fact]
  public void UnknownCommandPrintsErrorAndUsageToTheErrorStream() {
    var (code, output, error) = Run(new[] { "auth", "frobnicate" });

    code.Should().Be(2);
    output.Should().BeEmpty();
    error.Should().Contain("Usage:");
  }

  [Fact]
  public void MissingDataDirValueGivesExitCodeTwo() {
    using var output = new StringWriter();
    using var error = new StringWriter();

    AuthCommand.Run(new[] { "auth", "list", "--data-dir" }, output, error, Config(true, null), _clock).Should().Be(2);
  }
}
