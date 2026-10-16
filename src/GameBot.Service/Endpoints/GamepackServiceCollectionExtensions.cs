using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
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
      sp.GetService<ILogger<GamepackService>>(),
      sp.GetRequiredService<GamepackSignatureChecker>()));
    return services;
  }
}
