using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.Domain.Images;

namespace GameBot.Service.Endpoints;

/// <summary>Registers the gamepack services (feature 143).</summary>
internal static class GamepackServiceCollectionExtensions {
  /// <summary>
  /// Adds the registry store, the no-op write gate, the signature settings store, the verifier, the checker, the validator,
  /// the applier, and the service.
  /// A test can replace <see cref="IGamepackWriteGate"/> to inject a fault.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="dataRoot">The data root.</param>
  public static IServiceCollection AddGamepacks(this IServiceCollection services, string dataRoot) {
    ArgumentNullException.ThrowIfNull(services);
    services.AddSingleton<IGamepackRegistryStore>(_ => new GamepackRegistryStore(dataRoot));
    services.AddSingleton<IGamepackWriteGate, NoOpGamepackWriteGate>();
    services.AddSingleton<IGamepackSignatureSettingsStore>(_ => new GamepackSignatureSettingsStore(dataRoot));
    services.AddSingleton<IGamepackSignatureVerifier, GamepackSignatureVerifier>();
    services.AddSingleton(sp => new GamepackSignatureChecker(
      sp.GetRequiredService<IGamepackSignatureSettingsStore>(),
      sp.GetRequiredService<IGamepackSignatureVerifier>(),
      sp.GetService<ILogger<GamepackSignatureChecker>>()));
    services.AddSingleton(sp => new GamepackValidator(
      sp.GetRequiredService<ISequenceRepository>(),
      sp.GetRequiredService<ICommandRepository>(),
      sp.GetRequiredService<IImageRepository>(),
      sp.GetRequiredService<IGamepackRegistryStore>()));
    services.AddSingleton<ITakeOverBackupStore>(sp => new TakeOverBackupStore(dataRoot, sp.GetService<TimeProvider>()));
    services.AddSingleton(sp => new GamepackApplier(
      sp.GetRequiredService<ISequenceRepository>(),
      sp.GetRequiredService<ICommandRepository>(),
      sp.GetRequiredService<IImageRepository>(),
      sp.GetRequiredService<IGamepackRegistryStore>(),
      sp.GetRequiredService<IGamepackWriteGate>(),
      sp.GetService<ILogger<GamepackApplier>>(),
      sp.GetService<TimeProvider>(),
      sp.GetRequiredService<ITakeOverBackupStore>()));
    services.AddSingleton(sp => new GamepackService(
      sp.GetRequiredService<ISequenceRepository>(),
      sp.GetRequiredService<ICommandRepository>(),
      sp.GetRequiredService<IImageRepository>(),
      sp.GetRequiredService<IGamepackRegistryStore>(),
      sp.GetRequiredService<GamepackValidator>(),
      sp.GetRequiredService<GamepackApplier>(),
      sp.GetService<ILogger<GamepackService>>(),
      sp.GetRequiredService<GamepackSignatureChecker>()));
    services.AddGamepackCatalog(dataRoot);
    return services.AddManagedGamepackUpdate(dataRoot);
  }

  /// <summary>
  /// Adds the managed gamepack update (feature 148). The orchestrator is a singleton, so its lock is one lock for the whole
  /// instance. The orchestrator is resolved on the first managed call. The queue settings services must be registered too.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="dataRoot">The data root.</param>
  internal static IServiceCollection AddManagedGamepackUpdate(this IServiceCollection services, string dataRoot) {
    ArgumentNullException.ThrowIfNull(services);
    services.AddSingleton(new ManagedUpdateOptions());
    services.AddSingleton<IManagedBackupStore>(sp => new ManagedBackupStore(dataRoot, sp.GetService<TimeProvider>()));
    services.AddSingleton<IManagedQueueControl>(sp => new GameBot.Service.Services.QueueExecution.ManagedQueueControl(
      sp.GetRequiredService<GameBot.Domain.Queues.IQueueRepository>(),
      sp.GetRequiredService<GameBot.Service.Services.QueueExecution.IQueueExecutionService>()));
    services.AddSingleton(sp => new GamepackUpdateOrchestrator(
      sp.GetRequiredService<GamepackService>(),
      sp.GetRequiredService<IManagedQueueControl>(),
      sp.GetRequiredService<GameBot.Domain.QueueTemplates.IQueueTemplateRepository>(),
      sp.GetRequiredService<GameBot.Domain.QueueSettings.QueueSettingsExporter>(),
      sp.GetRequiredService<GameBot.Domain.QueueSettings.QueueSettingsImporter>(),
      sp.GetRequiredService<IManagedBackupStore>(),
      sp.GetRequiredService<ManagedUpdateOptions>(),
      sp.GetService<ILogger<GamepackUpdateOrchestrator>>()));
    return services;
  }

  /// <summary>
  /// Adds the catalog services (feature 147). No hosted service, no timer, and no cache use these types (FR-015).
  /// The catalog HTTP client does not follow redirects. The source follows them by hand and has its own time limit.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="dataRoot">The data root.</param>
  internal static IServiceCollection AddGamepackCatalog(this IServiceCollection services, string dataRoot) {
    ArgumentNullException.ThrowIfNull(services);
    services.AddHttpClient(CatalogHttpClientName, client => client.Timeout = System.Threading.Timeout.InfiniteTimeSpan)
      .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
    services.AddSingleton(new CatalogOptions());
    services.AddSingleton<IGamepackCatalogSettingsStore>(_ => new GamepackCatalogSettingsStore(dataRoot));
    services.AddSingleton(sp => new GamepackCatalogSourceFactory(
      sp.GetRequiredService<IGamepackCatalogSettingsStore>(),
      () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(CatalogHttpClientName),
      sp.GetRequiredService<CatalogOptions>()));
    services.AddSingleton(sp => new GamepackCatalogService(
      sp.GetRequiredService<GamepackCatalogSourceFactory>(),
      sp.GetRequiredService<GamepackService>(),
      sp.GetRequiredService<CatalogOptions>(),
      sp.GetService<ILogger<GamepackCatalogService>>()));
    return services;
  }

  /// <summary>The name of the HTTP client of the catalog.</summary>
  internal const string CatalogHttpClientName = "gamepack-catalog";
}
