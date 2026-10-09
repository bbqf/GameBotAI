using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Service.Services.Updates;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdaterLauncherTests : IDisposable {
  private sealed class FakeStarter : IUpdaterProcessStarter {
    public string? Exe { get; private set; }
    public IReadOnlyList<string>? Arguments { get; private set; }
    public string? WorkingDirectory { get; private set; }
    public Exception? Failure { get; set; }

    public void StartDetached(string exePath, IReadOnlyList<string> arguments, string workingDirectory) {
      if (Failure is not null) {
        throw Failure;
      }

      Exe = exePath;
      Arguments = arguments;
      WorkingDirectory = workingDirectory;
    }
  }

  private readonly string _root = UpdateTestData.NewTempDirectory();
  private readonly string _installFolder;
  private readonly UpdatePaths _paths;
  private readonly FakeStarter _starter = new();

  public UpdaterLauncherTests() {
    _installFolder = Path.Combine(_root, "app");
    _paths = new UpdatePaths(Path.Combine(_root, "data"));
    var updater = Path.Combine(_installFolder, "updater");
    Directory.CreateDirectory(Path.Combine(updater, "sub"));
    File.WriteAllText(Path.Combine(updater, "GameBot.Updater.exe"), "exe");
    File.WriteAllText(Path.Combine(updater, "sub", "extra.dll"), "dll");
  }

  public void Dispose() {
    Directory.Delete(_root, recursive: true);
    GC.SuppressFinalize(this);
  }

  private UpdaterLauncher Launcher(IDictionary<string, string?>? settings = null) =>
    new(
      _paths,
      _starter,
      new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build(),
      _installFolder);

  private static UpdaterLaunchRequest Request() => new(
    Guid.Parse("6f1c0000-0000-0000-0000-000000000001"),
    @"C:\data\updates\GameBot-1.7.0.430.msi",
    "1.7.0.412",
    "1.7.0.430",
    new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero));

  [Fact]
  public void LaunchCopiesTheUpdaterFilesAndStartsTheCopy() {
    Launcher().Launch(Request());

    _starter.Exe.Should().Be(Path.Combine(_paths.UpdaterCopyDirectory, "GameBot.Updater.exe"));
    _starter.WorkingDirectory.Should().Be(_paths.UpdaterCopyDirectory);
    File.Exists(Path.Combine(_paths.UpdaterCopyDirectory, "GameBot.Updater.exe")).Should().BeTrue();
    File.Exists(Path.Combine(_paths.UpdaterCopyDirectory, "sub", "extra.dll")).Should().BeTrue();
  }

  [Fact]
  public void LaunchReplacesAnOldCopy() {
    Directory.CreateDirectory(_paths.UpdaterCopyDirectory);
    File.WriteAllText(Path.Combine(_paths.UpdaterCopyDirectory, "stale.dll"), "x");

    Launcher().Launch(Request());

    File.Exists(Path.Combine(_paths.UpdaterCopyDirectory, "stale.dll")).Should().BeFalse();
  }

  [Fact]
  public void CommandLineHasEveryValueTheUpdaterNeeds() {
    Launcher().Launch(Request());

    var parsed = GameBot.Updater.UpdaterArguments.TryParse(_starter.Arguments!.ToList(), out var error);
    error.Should().BeNull();
    parsed!.BotPid.Should().Be(Environment.ProcessId);
    parsed.MsiPath.Should().Be(Request().MsiPath);
    parsed.InstallFolder.Should().Be(_installFolder);
    parsed.DataRoot.Should().Be(_paths.DataRoot);
    parsed.ResultPath.Should().Be(_paths.ResultFile);
    parsed.LogPath.Should().Be(_paths.MsiexecLogPath(Request().AttemptId));
    parsed.AttemptId.Should().Be("6f1c0000-0000-0000-0000-000000000001");
    parsed.FromVersion.Should().Be("1.7.0.412");
    parsed.TargetVersion.Should().Be("1.7.0.430");
    parsed.StartedAtUtc.Should().Be(Request().StartedAtUtc);
  }

  [Fact]
  public void SavedNetworkValuesArePassedWhenKnown() {
    Launcher(new Dictionary<string, string?> {
      ["Service:Network:Port"] = "8088",
      ["Service:Network:BindHost"] = "0.0.0.0"
    }).Launch(Request());

    var parsed = GameBot.Updater.UpdaterArguments.TryParse(_starter.Arguments!.ToList(), out _);
    parsed!.Port.Should().Be("8088");
    parsed.BindHost.Should().Be("0.0.0.0");
  }

  [Fact]
  public void MissingUpdaterFolderFailsWithACode() {
    Directory.Delete(Path.Combine(_installFolder, "updater"), recursive: true);

    var act = () => Launcher().Launch(Request());

    act.Should().Throw<UpdateFailureException>().Which.Error.Code.Should().Be("update_install_failed");
  }

  [Fact]
  public void ProcessThatDoesNotStartFailsWithACode() {
    _starter.Failure = new InvalidOperationException("no start");

    var act = () => Launcher().Launch(Request());

    act.Should().Throw<UpdateFailureException>().Which.Error.Hint.Should().NotBeNullOrWhiteSpace();
  }

  [Fact]
  public void RegistrationConstructorUsesTheBaseDirectory() {
    var launcher = new UpdaterLauncher(_paths, _starter, new ConfigurationBuilder().Build());

    var act = () => launcher.BuildArguments(Request());

    act.Should().NotThrow();
  }
}
