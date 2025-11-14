using System.Text.Json;
using GameBot.Service.Models;

namespace GameBot.Service.Services;

internal interface IConfigSnapshotService
{
    ConfigurationSnapshot? Current { get; }
    Task<ConfigurationSnapshot> RefreshAsync(CancellationToken ct = default);
}

internal sealed class ConfigSnapshotService : IConfigSnapshotService, IDisposable
{
    private readonly string _storageRoot;
    private readonly string _configDir;
    private readonly string _configFile;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _refreshCount;

    public ConfigurationSnapshot? Current { get; private set; }

    public ConfigSnapshotService(string storageRoot)
    {
        _storageRoot = storageRoot;
        _configDir = Path.Combine(_storageRoot, "config");
        _configFile = Path.Combine(_configDir, "config.json");
    }

    public async Task<ConfigurationSnapshot> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var baseline = await LoadSavedConfigAsync(ct).ConfigureAwait(false);
            var env = LoadEnvironment();
            var merged = MergeWithPrecedence(
                defaults: new Dictionary<string, object?>(),
                otherFiles: new Dictionary<string, object?>(),
                saved: baseline,
                env: env);

            // Apply masking and special rules
            var parameters = new Dictionary<string, ConfigurationParameter>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in merged)
            {
                var name = kvp.Key;
                var value = kvp.Value;
                var isSecret = ConfigurationMasking.IsSecretKey(name);
                object? masked = ConfigurationMasking.MaskIfSecret(name, value);

                // Exclude absolute storage path value from persisted output (represent as null)
                if (masked is string s && !string.IsNullOrWhiteSpace(s))
                {
                    try
                    {
                        if (string.Equals(Path.GetFullPath(s), Path.GetFullPath(_storageRoot), StringComparison.OrdinalIgnoreCase))
                        {
                            masked = null;
                        }
                    }
                    catch
                    {
                        // If path is invalid/empty, ignore comparison and keep original value
                    }
                }

                parameters[name] = new ConfigurationParameter
                {
                    Name = name,
                    Source = DetermineSource(name, env, baseline),
                    Value = masked,
                    IsSecret = isSecret
                };
            }

            var snapshot = new ConfigurationSnapshot
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                ServiceVersion = typeof(ConfigSnapshotService).Assembly.GetName().Version?.ToString(),
                DynamicPort = null,
                RefreshCount = _refreshCount++,
                Parameters = parameters
            };

            await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
            Current = snapshot;
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, object?>> LoadSavedConfigAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(_configFile)) return new Dictionary<string, object?>();
            using var fs = File.OpenRead(_configFile);
            var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("parameters", out var parametersEl) && parametersEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in parametersEl.EnumerateObject())
                {
                    var name = prop.Name;
                    var valEl = prop.Value;
                    object? value = null;
                    if (valEl.ValueKind == JsonValueKind.Object && valEl.TryGetProperty("value", out var v2))
                    {
                        value = ExtractJsonValue(v2);
                    }
                    else
                    {
                        value = ExtractJsonValue(valEl);
                    }
                    dict[name] = value;
                }
            }
            return dict;
        }
        catch
        {
            // Malformed or unreadable file: ignore per FR-013
            return new Dictionary<string, object?>();
        }
    }

    private static object? ExtractJsonValue(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el.ToString()
        };
    }

    private static Dictionary<string, object?> LoadEnvironment()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            var key = e.Key?.ToString();
            if (string.IsNullOrEmpty(key)) continue;
            dict[key] = e.Value?.ToString();
        }
        return dict;
    }

    private static Dictionary<string, object?> MergeWithPrecedence(
        Dictionary<string, object?> defaults,
        Dictionary<string, object?> otherFiles,
        Dictionary<string, object?> saved,
        Dictionary<string, object?> env)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        void Apply(Dictionary<string, object?> src)
        {
            foreach (var kv in src)
            {
                result[kv.Key] = kv.Value;
            }
        }

        Apply(defaults);
        Apply(otherFiles);
        Apply(saved);
        Apply(env);
        return result;
    }

    private static string DetermineSource(string name, Dictionary<string, object?> env, Dictionary<string, object?> saved)
    {
        if (env.ContainsKey(name)) return "Environment";
        if (saved.ContainsKey(name)) return "File";
        return "Default";
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private async Task PersistSnapshotAsync(ConfigurationSnapshot snapshot, CancellationToken ct)
    {
        Directory.CreateDirectory(_configDir);
        var tmp = _configFile + ".tmp";

        var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);

        await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
        if (File.Exists(_configFile))
        {
            File.Delete(_configFile);
        }
        File.Move(tmp, _configFile, true);
    }

    public void Dispose()
    {
        _gate.Dispose();
    }
}
