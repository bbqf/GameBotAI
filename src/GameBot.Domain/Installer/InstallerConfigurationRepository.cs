using System.Text.Json;

namespace GameBot.Domain.Installer;

public sealed class InstallerConfigurationRepository : IInstallerConfigurationRepository {
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
  private readonly string _path;

  public InstallerConfigurationRepository(string root) {
    ArgumentException.ThrowIfNullOrWhiteSpace(root);
    var cfgDir = Path.Combine(root, "config");
    Directory.CreateDirectory(cfgDir);
    _path = Path.Combine(cfgDir, "installer-config.json");
  }

  public async Task<InstallationProfile?> GetAsync(CancellationToken ct = default) {
    if (!File.Exists(_path)) {
      return null;
    }

    using var stream = File.OpenRead(_path);
    return await JsonSerializer.DeserializeAsync<InstallationProfile>(stream, JsonOptions, ct).ConfigureAwait(false);
  }

  public async Task<InstallationProfile> SaveAsync(InstallationProfile profile, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(profile);
    profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
    if (profile.CreatedAtUtc == default) {
      profile.CreatedAtUtc = profile.UpdatedAtUtc;
    }

    using var stream = File.Create(_path);
    await JsonSerializer.SerializeAsync(stream, profile, JsonOptions, ct).ConfigureAwait(false);
    return profile;
  }
}
