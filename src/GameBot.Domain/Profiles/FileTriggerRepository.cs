using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GameBot.Domain.Profiles;

public sealed class FileTriggerRepository : ITriggerRepository
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public FileTriggerRepository(string root)
    {
        _dir = Path.Combine(root, "triggers");
        Directory.CreateDirectory(_dir);
    }

    public async Task<ProfileTrigger?> GetAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_dir, id + ".json");
        if (!File.Exists(path)) return null;
        using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProfileTrigger>(fs, JsonOpts, ct).ConfigureAwait(false);
    }

    public async Task UpsertAsync(ProfileTrigger trigger, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (string.IsNullOrWhiteSpace(trigger.Id)) throw new ArgumentException("Trigger.Id is required");
        var path = Path.Combine(_dir, trigger.Id + ".json");
        using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, trigger, JsonOpts, ct).ConfigureAwait(false);
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_dir, id + ".json");
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }
}
