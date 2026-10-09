using FluentAssertions;
using GameBot.Service.Hosted;
using GameBot.Service.Services.Updates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdateServiceRegistrationTests : IDisposable {
  private readonly string _root = UpdateTestData.NewTempDirectory();
  private readonly FakeLifetime _lifetime = new();

  public void Dispose() {
    _lifetime.Dispose();
    Directory.Delete(_root, recursive: true);
    GC.SuppressFinalize(this);
  }

  private ServiceProvider Build(IDictionary<string, string?>? settings = null) {
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(configuration);
    services.AddSingleton<IHostApplicationLifetime>(_lifetime);
    services.AddUpdateServices(configuration, _root);
    return services.BuildServiceProvider();
  }

  [Fact]
  public void EveryUpdateServiceResolves() {
    using var provider = Build();

    provider.GetRequiredService<IReleaseClient>().Should().BeOfType<GitHubReleaseClient>();
    provider.GetRequiredService<IUpdateDownloader>().Should().BeOfType<UpdateDownloader>();
    provider.GetRequiredService<IUpdaterLauncher>().Should().BeOfType<UpdaterLauncher>();
    provider.GetRequiredService<IUpdateResultStore>().Should().BeOfType<UpdateResultStore>();
    provider.GetRequiredService<IInstalledVersionProvider>().Should().BeOfType<InstalledVersionProvider>();
    provider.GetRequiredService<IInstallLocationGuard>().Should().BeOfType<InstallLocationGuard>();
    provider.GetRequiredService<IFreeSpaceProvider>().Should().BeOfType<SystemFreeSpaceProvider>();
    provider.GetRequiredService<IUpdateQueueStopper>().Should().BeOfType<UpdateQueueStopper>();
    provider.GetRequiredService<UpdateCheckService>().Should().NotBeNull();
    provider.GetRequiredService<UpdateCoordinator>().Should().NotBeNull();
    provider.GetRequiredService<UpdateResultReporter>().Should().NotBeNull();
  }

  [Fact]
  public void ServicesAreSingletons() {
    using var provider = Build();

    provider.GetRequiredService<UpdateCoordinator>().Should().BeSameAs(provider.GetRequiredService<UpdateCoordinator>());
    provider.GetRequiredService<IReleaseClient>().Should().BeSameAs(provider.GetRequiredService<IReleaseClient>());
  }

  [Fact]
  public void PathsPointToTheUpdatesFolderOfTheDataRoot() {
    using var provider = Build();

    provider.GetRequiredService<UpdatePaths>().Directory.Should().Be(Path.Combine(_root, "updates"));
  }

  [Fact]
  public void OptionsDefaultToThePublicRepository() {
    using var provider = Build();

    var options = provider.GetRequiredService<IOptions<UpdateOptions>>().Value;

    options.Repository.Should().Be("bbqf/GameBotAI");
    options.ApiBaseUrl.Should().Be(new Uri("https://api.github.com"));
  }

  [Fact]
  public void OptionsReadTheUpdateSection() {
    using var provider = Build(new Dictionary<string, string?> {
      ["Update:Repository"] = "me/fork",
      ["Update:ApiBaseUrl"] = "http://localhost:5999"
    });

    var options = provider.GetRequiredService<IOptions<UpdateOptions>>().Value;

    options.Repository.Should().Be("me/fork");
    options.ApiBaseUrl.Should().Be(new Uri("http://localhost:5999"));
  }

  [Fact]
  public void ResultReportingServiceIsAHostedService() {
    using var provider = Build();

    provider.GetServices<IHostedService>().Should().ContainSingle(s => s is UpdateResultReportingService);
  }

  [Fact]
  public void LoggerFactoryIsUsable() {
    using var provider = Build();

    provider.GetRequiredService<ILoggerFactory>().Should().NotBeNull();
  }
}
