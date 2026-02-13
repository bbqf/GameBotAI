namespace GameBot.Domain.Installer;

public interface IInstallerConfigurationRepository {
  Task<InstallationProfile?> GetAsync(CancellationToken ct = default);
  Task<InstallationProfile> SaveAsync(InstallationProfile profile, CancellationToken ct = default);
}
