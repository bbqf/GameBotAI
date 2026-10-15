using GameBot.Domain.Commands;
using GameBot.Domain.Games;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Images;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueSettings;
using GameBot.Domain.QueueTemplates;

namespace GameBot.Service.Endpoints;

/// <summary>Registers the queue settings export and import (feature 145).</summary>
internal static class QueueSettingsServiceCollectionExtensions {
  /// <summary>
  /// Adds the exporter and the importer. The gamepack services must be registered too, because the importer takes the
  /// gamepack gate (FR-017).
  /// </summary>
  /// <param name="services">The service collection.</param>
  public static IServiceCollection AddQueueSettings(this IServiceCollection services) {
    ArgumentNullException.ThrowIfNull(services);
    services.AddSingleton(sp => new QueueSettingsExporter(
      sp.GetRequiredService<ISequenceRepository>(),
      sp.GetRequiredService<ICommandRepository>(),
      sp.GetRequiredService<IQueueRepository>(),
      sp.GetRequiredService<IQueueRuntimeStore>(),
      sp.GetRequiredService<IQueueTemplateRepository>(),
      sp.GetRequiredService<IGameRepository>(),
      sp.GetRequiredService<GamepackService>(),
      sp.GetService<TimeProvider>()));
    services.AddSingleton(sp => new QueueSettingsImporter(
      sp.GetRequiredService<ISequenceRepository>(),
      sp.GetRequiredService<ICommandRepository>(),
      sp.GetRequiredService<IQueueRepository>(),
      sp.GetRequiredService<IQueueRuntimeStore>(),
      sp.GetRequiredService<IQueueTemplateRepository>(),
      sp.GetRequiredService<IGameRepository>(),
      sp.GetRequiredService<IImageRepository>(),
      sp.GetRequiredService<GamepackService>(),
      sp.GetService<TimeProvider>()));
    return services;
  }
}
