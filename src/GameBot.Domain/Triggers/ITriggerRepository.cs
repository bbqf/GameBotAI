namespace GameBot.Domain.Triggers;

public interface ITriggerRepository
{
    Task<Trigger?> GetAsync(string id, CancellationToken ct = default);
    Task UpsertAsync(Trigger trigger, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default);
}
