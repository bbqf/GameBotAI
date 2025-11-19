using System.Text.Json;

namespace GameBot.Domain.Commands;

public sealed class FileCommandRepository : ICommandRepository
{
    private readonly string _dir;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public FileCommandRepository(string root)
    {
        _dir = Path.Combine(root, "commands");
        Directory.CreateDirectory(_dir);
    }

    public async Task<Command> AddAsync(Command command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = string.IsNullOrWhiteSpace(command.Id) ? Guid.NewGuid().ToString("N") : command.Id;
        command.Id = id;
        var path = Path.Combine(_dir, id + ".json");
        using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, command, JsonOpts, ct).ConfigureAwait(false);
        return command;
    }

    public async Task<Command?> GetAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_dir, id + ".json");
        if (!File.Exists(path)) return null;
        using var fs = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Command>(fs, JsonOpts, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default)
    {
        var list = new List<Command>();
        foreach (var file in Directory.EnumerateFiles(_dir, "*.json"))
        {
            using var fs = File.OpenRead(file);
            var item = await JsonSerializer.DeserializeAsync<Command>(fs, JsonOpts, ct).ConfigureAwait(false);
            if (item is not null) list.Add(item);
        }
        return list;
    }

    public async Task<Command?> UpdateAsync(Command command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Id)) return null;
        var path = Path.Combine(_dir, command.Id + ".json");
        if (!File.Exists(path)) return null;
        using var fs = File.Create(path);
        await JsonSerializer.SerializeAsync(fs, command, JsonOpts, ct).ConfigureAwait(false);
        return command;
    }

    public Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var path = Path.Combine(_dir, id + ".json");
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }
}
