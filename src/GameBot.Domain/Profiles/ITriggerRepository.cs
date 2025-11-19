namespace GameBot.Domain.Profiles;

public interface ITriggerRepository
{
    Task<ProfileTrigger?> GetAsync(string id, CancellationToken ct = default);
    Task UpsertAsync(ProfileTrigger trigger, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<ProfileTrigger>> ListAsync(CancellationToken ct = default);
}
