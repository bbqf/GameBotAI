using System.Text.Json;

namespace GameBot.Domain.Profiles;

public sealed class FileProfileRepository : IProfileRepository
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public FileProfileRepository(string root)
    {
        _dir = Path.Combine(root, "profiles");
        Directory.CreateDirectory(_dir);
    }

    public async Task<AutomationProfile> AddAsync(AutomationProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id;
        profile.Id = id;
        var path = Path.Combine(_dir, id + ".json");
        using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, profile, JsonOpts, ct).ConfigureAwait(false);
        return profile;
    }

    public async Task<AutomationProfile?> GetAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_dir, id + ".json");
        if (!File.Exists(path)) return null;
        using var fs = File.OpenRead(path);
    var prof = await JsonSerializer.DeserializeAsync<AutomationProfile>(fs, JsonOpts, ct).ConfigureAwait(false);
    prof ??= new AutomationProfile { Id = id, Name = id, GameId = string.Empty };
        return prof;
    }

    public async Task<IReadOnlyList<AutomationProfile>> ListAsync(string? gameId = null, CancellationToken ct = default)
    {
        var list = new List<AutomationProfile>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            using var fs = File.OpenRead(file);
            var item = await JsonSerializer.DeserializeAsync<AutomationProfile>(fs, JsonOpts, ct).ConfigureAwait(false);
            if (item is not null && (gameId is null || string.Equals(item.GameId, gameId, StringComparison.OrdinalIgnoreCase)))
                list.Add(item);
        }
        return list;
    }

    public async Task<AutomationProfile?> UpdateAsync(AutomationProfile profile, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Id)) return null;
        var path = Path.Combine(_dir, profile.Id + ".json");
        if (!File.Exists(path)) return null;
        using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, profile, JsonOpts, ct).ConfigureAwait(false);
        return profile;
    }
}
