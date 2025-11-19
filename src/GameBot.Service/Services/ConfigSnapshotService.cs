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
    private readonly IConfigApplier _applier;

    public ConfigurationSnapshot? Current { get; private set; }

    public ConfigSnapshotService(string storageRoot, IConfigApplier applier)
    {
        _storageRoot = storageRoot;
        _configDir = Path.Combine(_storageRoot, "config");
        _configFile = Path.Combine(_configDir, "config.json");
        _applier = applier;
    }

    public ConfigSnapshotService(string storageRoot)
        : this(storageRoot, new NoopConfigApplier())
    {
    }

    public async Task<ConfigurationSnapshot> RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var baseline = await LoadSavedConfigAsync(ct).ConfigureAwait(false);
            var envInfo = LoadEnvironmentFiltered();
            var env = envInfo.env;
            var defaults = BuildDefaultRelevantKeys();
            var merged = MergeWithPrecedence(
                defaults: defaults,
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
                EnvScanned = envInfo.scanned,
                EnvIncluded = envInfo.included,
                EnvExcluded = envInfo.excluded,
                Parameters = parameters
            };

            // Apply configuration to the running service before persisting
            _applier.Apply(snapshot);
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

    private static (Dictionary<string, object?> env, int scanned, int included, int excluded) LoadEnvironmentFiltered()
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        int scanned = 0, included = 0, excluded = 0;
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
        {
            scanned++;
            var key = e.Key?.ToString();
            if (string.IsNullOrEmpty(key)) { excluded++; continue; }
            if (!key.StartsWith("GAMEBOT_", StringComparison.OrdinalIgnoreCase)) { excluded++; continue; }
            // Treat null/empty values as absent so saved file can take precedence (Fix: config source detection)
            var rawVal = e.Value?.ToString();
            if (string.IsNullOrEmpty(rawVal)) { excluded++; continue; }
            dict[key] = rawVal;
            included++;
        }
        return (dict, scanned, included, excluded);
    }

    private Dictionary<string, object?> BuildDefaultRelevantKeys()
    {
        // Always include known GameBot-relevant env vars, even if not set
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            // GAMEBOT_* keys and their effective defaults
            ["GAMEBOT_DATA_DIR"] = _storageRoot,
            ["GAMEBOT_DYNAMIC_PORT"] = "false",
            ["GAMEBOT_AUTH_TOKEN"] = null, // secret; will be masked if set
            ["GAMEBOT_USE_ADB"] = "true", // default enabled unless explicitly "false"
            ["GAMEBOT_TEST_SCREEN_IMAGE_B64"] = null,
            ["GAMEBOT_DEBUG_DUMP_IMAGES"] = "false",
            ["GAMEBOT_TESSERACT_ENABLED"] = "false",
            ["GAMEBOT_TESSERACT_PATH"] = "tesseract",
            ["GAMEBOT_TESSERACT_LANG"] = "eng", // default language
            ["GAMEBOT_TESSERACT_PSM"] = null,
            ["GAMEBOT_TESSERACT_OEM"] = null,
            ["GAMEBOT_TEST_OCR_TEXT"] = "",
            ["GAMEBOT_TEST_OCR_CONF"] = 0.0,
            ["GAMEBOT_ADB_PATH"] = null,
            ["GAMEBOT_ADB_RETRIES"] = 2,
            ["GAMEBOT_ADB_RETRY_DELAY_MS"] = 100,
            ["GAMEBOT_HTTP_LOG_LEVEL_MINIMUM"] = "Warning",

            // ASP.NET Core configuration env keys and their documented defaults/effective values
            ["Service__Storage__Root"] = _storageRoot,
            ["Service__Auth__Token"] = null,
            ["Service__Sessions__MaxConcurrentSessions"] = 3,
            ["Service__Sessions__IdleTimeoutSeconds"] = 1800,
            ["Service__Triggers__Worker__IntervalSeconds"] = 2,
            ["Service__Triggers__Worker__GameFilter"] = null,
            ["Service__Triggers__Worker__SkipWhenNoSessions"] = "true",
            ["Service__Triggers__Worker__IdleBackoffSeconds"] = 5,
            ["Logging__LogLevel__Default"] = null,
        };
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
        if (env.TryGetValue(name, out var envVal) && !string.IsNullOrEmpty(envVal?.ToString())) return "Environment";
        if (saved.ContainsKey(name)) return "File";
        return "Default";
    }

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
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
