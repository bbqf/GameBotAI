namespace GameBot.Domain.Profiles;

public interface IProfileRepository
{
    Task<AutomationProfile> AddAsync(AutomationProfile profile, CancellationToken ct = default);
    Task<AutomationProfile?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<AutomationProfile>> ListAsync(string? gameId = null, CancellationToken ct = default);
    Task<AutomationProfile?> UpdateAsync(AutomationProfile profile, CancellationToken ct = default);
}
