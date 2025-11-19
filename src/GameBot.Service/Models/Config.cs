using System.Text.Json.Serialization;

namespace GameBot.Service.Models;

internal sealed class ConfigurationParameter {
  public required string Name { get; init; }
  public required string Source { get; init; } // Default | File | Environment
  public object? Value { get; init; } // Masked if secret
  public bool IsSecret { get; init; }
}

internal sealed class ConfigurationSnapshot {
  public required DateTimeOffset GeneratedAtUtc { get; init; }
  public string? ServiceVersion { get; init; }
  public int? DynamicPort { get; init; }
  public int RefreshCount { get; init; }
  public int EnvScanned { get; init; }
  public int EnvIncluded { get; init; }
  public int EnvExcluded { get; init; }

  public required Dictionary<string, ConfigurationParameter> Parameters { get; init; } = new();
}

internal static class ConfigurationMasking {
  private static readonly string[] SecretMarkers = new[] { "TOKEN", "SECRET", "PASSWORD", "KEY" };

  public static bool IsSecretKey(string key) {
    if (string.IsNullOrWhiteSpace(key)) return false;
    foreach (var marker in SecretMarkers) {
      if (key.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
    }
    return false;
  }

  public static object? MaskIfSecret(string key, object? value) {
    if (!IsSecretKey(key)) return value;
    return "***";
  }
}
