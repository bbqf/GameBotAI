using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Images;

namespace GameBot.Service.Endpoints;

/// <summary>Registers the gamepack services (feature 143).</summary>
internal static class GamepackServiceCollectionExtensions {
  /// <summary>
  /// Adds the registry store, the no-op write gate, the validator, the applier, and the service.
  /// A test can replace <see cref="IGamepackWriteGate"/> to inject a fault.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="dataRoot">The data root.</param>
  public static IServiceCollection AddGamepacks(this IServiceCollection services, string dataRoot) {
    ArgumentNullException.ThrowIfNull(services);
    services.AddSingleton<IGamepackRegistryStore>(_ => new GamepackRegistryStore(dataRoot));
    services.AddSingleton<IGamepackWriteGate, NoOpGamepackWriteGate>();
    services.AddSingleton(sp => new GamepackValidator(
      sp.GetRequiredService<ISequenceRepository>(),
      sp.GetRequiredService<ICommandRepository>(),
      sp.GetRequiredService<IImageRepository>(),
      sp.GetRequiredService<IGamepackRegistryStore>()));
    services.AddSingleton(sp => new GamepackApplier(
      sp.GetRequiredService<ISequenceRepository>(),
      sp.GetRequiredService<ICommandRepository>(),
      sp.GetRequiredService<IImageRepository>(),
      sp.GetRequiredService<IGamepackRegistryStore>(),
      sp.GetRequiredService<IGamepackWriteGate>(),
      sp.GetService<ILogger<GamepackApplier>>(),
      sp.GetService<TimeProvider>()));
    services.AddSingleton(sp => new GamepackService(
      sp.GetRequiredService<ISequenceRepository>(),
      sp.GetRequiredService<ICommandRepository>(),
      sp.GetRequiredService<IImageRepository>(),
      sp.GetRequiredService<IGamepackRegistryStore>(),
      sp.GetRequiredService<GamepackValidator>(),
      sp.GetRequiredService<GamepackApplier>(),
      sp.GetService<ILogger<GamepackService>>()));
    return services;
  }
}
