using System.Diagnostics.CodeAnalysis;
using GameBot.Service.Hosted;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace GameBot.Service.Services.Updates;

/// <summary>Registers the update services (feature 131). Kept out of GameBotServiceSetup to keep that file small.</summary>
internal static class UpdateServiceRegistration {
  public static IServiceCollection AddUpdateServices(this IServiceCollection services, IConfiguration configuration, string dataRoot) {
    services.Configure<UpdateOptions>(configuration.GetSection(UpdateOptions.SectionName));
    services.TryAddSingleton(TimeProvider.System);
    services.AddSingleton(new UpdatePaths(dataRoot));

    services.AddSingleton<IInstalledVersionProvider, InstalledVersionProvider>();
    services.AddSingleton<IInstallLocationGuard, InstallLocationGuard>();
    services.AddSingleton<IFreeSpaceProvider, SystemFreeSpaceProvider>();
    services.AddSingleton<IUpdateResultStore, UpdateResultStore>();
    services.AddSingleton<IUpdaterProcessStarter, ProcessUpdaterStarter>();
    services.AddSingleton<IUpdaterLauncher, UpdaterLauncher>();
    services.AddSingleton<IUpdateQueueStopper, UpdateQueueStopper>();
    services.AddSingleton<IReleaseClient>(CreateReleaseClient);
    services.AddSingleton<IUpdateDownloader>(CreateDownloader);
    services.AddSingleton<UpdateCheckService>();
    services.AddSingleton<UpdateCoordinator>();
    services.AddSingleton<UpdateResultReporter>();
    services.AddHostedService<UpdateResultReportingService>();
    return services;
  }

  private static GitHubReleaseClient CreateReleaseClient(IServiceProvider services) =>
    new(
      CreateHttpClient(TimeSpan.FromSeconds(15)),
      services.GetRequiredService<IOptions<UpdateOptions>>(),
      services.GetRequiredService<ILogger<GitHubReleaseClient>>());

  private static UpdateDownloader CreateDownloader(IServiceProvider services) =>
    new(CreateHttpClient(TimeSpan.FromMinutes(30)), services.GetRequiredService<UpdatePaths>());

  // The client must not follow redirects by itself: UpdateHttp checks the host of each step.
  [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The client owns the handler (disposeHandler: true). The singleton client lives as long as the host.")]
  private static HttpClient CreateHttpClient(TimeSpan timeout) {
    var client = new HttpClient(CreateHandler(), disposeHandler: true) { Timeout = timeout };
    client.DefaultRequestHeaders.UserAgent.ParseAdd(UpdateHttp.UserAgent);
    return client;
  }

  private static SocketsHttpHandler CreateHandler() => new() {
    AllowAutoRedirect = false,
    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
  };
}
