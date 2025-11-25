using Xunit;

namespace GameBot.IntegrationTests;

// Ensures tests that mutate persisted configuration or environment variables run sequentially.
[CollectionDefinition("ConfigIsolation", DisableParallelization = true)]
public sealed class ConfigIsolationDefinition { }
