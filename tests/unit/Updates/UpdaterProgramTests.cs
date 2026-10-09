using System.Text.Json;
using FluentAssertions;
using GameBot.Updater;
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace GameBot.UnitTests.Updates;

[SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "Test data. The arrays are small and used once per test.")]
public sealed class UpdaterProgramTests {
  private static readonly string[] FullLine = {
    "--bot-pid", "4242",
    "--msi", @"C:\data\updates\GameBot-1.7.0.430.msi",
    "--install-folder", @"C:\Users\anton\AppData\Local\GameBot\app",
    "--port", "8080",
    "--bind-host", "127.0.0.1",
    "--data-root", @"C:\data",
    "--result-path", @"C:\data\updates\update-result.json",
    "--log-path", @"C:\data\updates\msiexec-1.log",
    "--attempt-id", "6f1c0000-0000-0000-0000-000000000001",
    "--from-version", "1.7.0.412",
    "--target-version", "1.7.0.430",
    "--started-at", "2026-10-09T10:00:00+00:00"
  };

  private sealed class FakeHost : IUpdaterHost {
    public List<string> Steps { get; } = new();
    public bool BotExits { get; set; } = true;
    public int MsiexecExitCode { get; set; }
    public Exception? MsiexecFailure { get; set; }
    public bool BotStarts { get; set; } = true;
    public string? MsiexecArguments { get; private set; }
    public int StartBotCalls { get; private set; }
    public List<string> Results { get; } = new();
    public bool WriteFails { get; set; }
    public DateTimeOffset UtcNow { get; } = new(2026, 10, 9, 10, 5, 0, TimeSpan.Zero);

    public bool WaitForExit(int pid, TimeSpan timeout) {
      Steps.Add("wait");
      return BotExits;
    }

    public Task<int> RunMsiexecAsync(string arguments) {
      Steps.Add("msiexec");
      MsiexecArguments = arguments;
      return MsiexecFailure is null ? Task.FromResult(MsiexecExitCode) : Task.FromException<int>(MsiexecFailure);
    }

    public bool StartBot(string installFolder) {
      Steps.Add("startBot");
      StartBotCalls++;
      return BotStarts;
    }

    public void WriteResultFile(string path, string json) {
      Steps.Add("write");
      if (WriteFails) {
        throw new IOException("disk full");
      }
      Results.Add(json);
    }

    public JsonElement LastResult() => JsonDocument.Parse(Results[^1]).RootElement.Clone();
  }

  private static UpdaterArguments Args() => UpdaterArguments.TryParse(FullLine, out _)!;

  [Fact]
  public void FullCommandLineParses() {
    var args = UpdaterArguments.TryParse(FullLine, out var error);

    error.Should().BeNull();
    args!.BotPid.Should().Be(4242);
    args.MsiPath.Should().EndWith(".msi");
    args.InstallFolder.Should().EndWith(@"GameBot\app");
    args.Port.Should().Be("8080");
    args.BindHost.Should().Be("127.0.0.1");
    args.DataRoot.Should().Be(@"C:\data");
    args.AttemptId.Should().Be("6f1c0000-0000-0000-0000-000000000001");
    args.TargetVersion.Should().Be("1.7.0.430");
    args.StartedAtUtc.Year.Should().Be(2026);
  }

  [Fact]
  public void OptionalValuesCanBeMissing() {
    var line = new[] {
      "--bot-pid", "1", "--msi", "a.msi", "--install-folder", "C:\\app", "--result-path", "r.json",
      "--attempt-id", "id", "--target-version", "1.0.0.1"
    };

    var args = UpdaterArguments.TryParse(line, out var error);

    error.Should().BeNull();
    args!.Port.Should().BeEmpty();
    args.LogPath.Should().BeEmpty();
  }

  [Theory]
  [InlineData("bot-pid")]
  [InlineData("msi")]
  [InlineData("install-folder")]
  [InlineData("result-path")]
  [InlineData("attempt-id")]
  [InlineData("target-version")]
  public void MissingRequiredValueIsAnError(string name) {
    var list = FullLine.ToList();
    var index = list.IndexOf("--" + name);
    list.RemoveRange(index, 2);

    UpdaterArguments.TryParse(list, out var error).Should().BeNull();

    error.Should().Contain(name);
  }

  [Theory]
  [InlineData("--bot-pid", "abc")]
  [InlineData("--bot-pid", "-5")]
  [InlineData("--started-at", "not a date")]
  public void BadValueIsAnError(string name, string value) {
    var list = FullLine.ToList();
    list[list.IndexOf(name) + 1] = value;

    UpdaterArguments.TryParse(list, out var error).Should().BeNull();

    error.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void ArgumentWithoutADashPrefixIsAnError() {
    UpdaterArguments.TryParse(new[] { "stray" }, out var error).Should().BeNull();
    error.Should().Contain("stray");
  }

  [Fact]
  public void NameWithoutAValueIsAnError() {
    UpdaterArguments.TryParse(new[] { "--msi" }, out var error).Should().BeNull();
    error.Should().Contain("--msi");
  }

  [Fact]
  public void MsiexecLineHasTheSilentSwitchesAndProperties() {
    var line = MsiexecArguments.Build(Args());

    line.Should().StartWith("/i \"C:\\data\\updates\\GameBot-1.7.0.430.msi\" /qn /norestart");
    line.Should().Contain("/l*v \"C:\\data\\updates\\msiexec-1.log\"");
    line.Should().Contain("APPLICATIONFOLDER=\"C:\\Users\\anton\\AppData\\Local\\GameBot\\app\"");
    line.Should().Contain("MSIINSTALLPERUSER=1");
    line.Should().Contain("PORT=\"8080\"");
    line.Should().Contain("BIND_HOST=\"127.0.0.1\"");
  }

  [Fact]
  public void MsiexecLineLeavesOutEmptyOptionalValues() {
    var args = Args() with { Port = "", BindHost = "", LogPath = "" };

    var line = MsiexecArguments.Build(args);

    line.Should().NotContain("PORT=");
    line.Should().NotContain("BIND_HOST=");
    line.Should().NotContain("/l*v");
  }

  [Fact]
  public void TrailingSlashOfTheInstallFolderIsRemoved() {
    var args = Args() with { InstallFolder = @"C:\app\" };

    MsiexecArguments.Build(args).Should().Contain("APPLICATIONFOLDER=\"C:\\app\"");
  }

  [Theory]
  [InlineData(0, true)]
  [InlineData(3010, true)]
  [InlineData(1603, false)]
  [InlineData(1, false)]
  public void OnlyZeroAnd3010AreSuccess(int code, bool success) {
    MsiexecArguments.IsSuccess(code).Should().Be(success);
  }

  [Theory]
  [InlineData(1603, "1603")]
  [InlineData(1618, "1618")]
  [InlineData(1925, "1925")]
  [InlineData(1234, "1234")]
  public void ExitCodesHaveAMessageAndAHint(int code, string text) {
    var (message, hint) = MsiexecArguments.Describe(code);

    message.Should().Contain(text);
    hint.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public async Task SuccessRunsTheStepsInOrderAndWritesSucceeded() {
    var host = new FakeHost();

    var exit = await new UpdaterRunner(host).RunAsync(Args()).ConfigureAwait(true);

    exit.Should().Be(UpdaterRunner.ExitSucceeded);
    host.Steps.Should().ContainInOrder("wait", "msiexec", "write", "startBot", "write");
    host.MsiexecArguments.Should().Contain("/qn");
    host.LastResult().GetProperty("state").GetString().Should().Be("succeeded");
    host.LastResult().GetProperty("finishedAtUtc").GetString().Should().NotBeNull();
    host.LastResult().GetProperty("targetVersion").GetString().Should().Be("1.7.0.430");
    host.LastResult().TryGetProperty("errorCode", out _).Should().BeFalse();
  }

  [Fact]
  public async Task RestartingStateIsWrittenBeforeTheBotStarts() {
    var host = new FakeHost();

    await new UpdaterRunner(host).RunAsync(Args()).ConfigureAwait(true);

    JsonDocument.Parse(host.Results[0]).RootElement.GetProperty("state").GetString().Should().Be("restarting");
  }

  [Fact]
  public async Task RebootRequiredCodeCountsAsSuccess() {
    var host = new FakeHost { MsiexecExitCode = 3010 };

    var exit = await new UpdaterRunner(host).RunAsync(Args()).ConfigureAwait(true);

    exit.Should().Be(UpdaterRunner.ExitSucceeded);
    host.LastResult().GetProperty("msiexecExitCode").GetInt32().Should().Be(3010);
  }

  [Fact]
  public async Task BotThatDoesNotExitStopsBeforeMsiexec() {
    var host = new FakeHost { BotExits = false };

    var exit = await new UpdaterRunner(host).RunAsync(Args()).ConfigureAwait(true);

    exit.Should().Be(UpdaterRunner.ExitFailed);
    host.Steps.Should().NotContain("msiexec");
    host.StartBotCalls.Should().Be(0);
    host.LastResult().GetProperty("state").GetString().Should().Be("failed");
    host.LastResult().GetProperty("errorCode").GetString().Should().Be("update_install_failed");
  }

  [Fact]
  public async Task NoBotPidSkipsTheWait() {
    var host = new FakeHost();

    await new UpdaterRunner(host).RunAsync(Args() with { BotPid = 0 }).ConfigureAwait(true);

    host.Steps.Should().NotContain("wait");
  }

  [Fact]
  public async Task MsiexecFailureWritesFailedAndStartsTheOldBot() {
    var host = new FakeHost { MsiexecExitCode = 1603 };

    var exit = await new UpdaterRunner(host).RunAsync(Args()).ConfigureAwait(true);

    exit.Should().Be(UpdaterRunner.ExitFailed);
    host.StartBotCalls.Should().Be(1);
    var result = host.LastResult();
    result.GetProperty("state").GetString().Should().Be("failed");
    result.GetProperty("errorCode").GetString().Should().Be("update_install_failed");
    result.GetProperty("msiexecExitCode").GetInt32().Should().Be(1603);
    result.GetProperty("errorHint").GetString().Should().NotBeNullOrWhiteSpace();
    result.GetProperty("logPath").GetString().Should().EndWith("msiexec-1.log");
  }

  [Fact]
  public async Task MsiexecThatCannotStartWritesFailedAndStartsTheOldBot() {
    var host = new FakeHost { MsiexecFailure = new InvalidOperationException("no msiexec") };

    var exit = await new UpdaterRunner(host).RunAsync(Args()).ConfigureAwait(true);

    exit.Should().Be(UpdaterRunner.ExitFailed);
    host.StartBotCalls.Should().Be(1);
    host.LastResult().GetProperty("errorCode").GetString().Should().Be("update_install_failed");
    host.LastResult().TryGetProperty("msiexecExitCode", out _).Should().BeFalse();
  }

  [Fact]
  public async Task BotThatDoesNotStartWritesRestartFailed() {
    var host = new FakeHost { BotStarts = false };

    var exit = await new UpdaterRunner(host).RunAsync(Args()).ConfigureAwait(true);

    exit.Should().Be(UpdaterRunner.ExitFailed);
    host.LastResult().GetProperty("state").GetString().Should().Be("failed");
    host.LastResult().GetProperty("errorCode").GetString().Should().Be("update_restart_failed");
    host.LastResult().GetProperty("msiexecExitCode").GetInt32().Should().Be(0);
  }

  [Fact]
  public async Task ResultFileThatCannotBeWrittenDoesNotCrashTheUpdater() {
    var host = new FakeHost { WriteFails = true };

    var exit = await new UpdaterRunner(host).RunAsync(Args()).ConfigureAwait(true);

    exit.Should().Be(UpdaterRunner.ExitSucceeded);
  }

  [Fact]
  public void ResultJsonUsesCamelCaseNames() {
    var json = new UpdaterResult { AttemptId = "id", State = UpdaterResult.Failed, ErrorCode = "c" }.ToJson();

    json.Should().Contain("\"attemptId\"");
    json.Should().Contain("\"state\": \"failed\"");
    json.Should().NotContain("finishedAtUtc");
  }
}
