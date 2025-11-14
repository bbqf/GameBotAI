using GameBot.Service.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameBot.Service.Services;

internal interface IConfigApplier
{
    void Apply(ConfigurationSnapshot snapshot);
}

internal sealed class ConfigApplier : IConfigApplier
{
    private readonly IOptionsMonitorCache<GameBot.Service.Hosted.TriggerWorkerOptions> _triggerOptionsCache;

    public ConfigApplier(IOptionsMonitorCache<GameBot.Service.Hosted.TriggerWorkerOptions> triggerOptionsCache)
    {
        _triggerOptionsCache = triggerOptionsCache;
    }

    private static LogLevel ParseLogLevel(string? v, LogLevel @default)
    {
        return v?.Trim().ToLowerInvariant() switch
        {
            "trace" => LogLevel.Trace,
            "debug" => LogLevel.Debug,
            "information" => LogLevel.Information,
            "info" => LogLevel.Information,
            "warning" => LogLevel.Warning,
            "warn" => LogLevel.Warning,
            "error" => LogLevel.Error,
            "critical" => LogLevel.Critical,
            _ => @default
        };
    }

    public void Apply(ConfigurationSnapshot snapshot)
    {
        // Apply dynamic HTTP logging minimum level
        if (snapshot.Parameters.TryGetValue("GAMEBOT_HTTP_LOG_LEVEL_MINIMUM", out var lvlParam))
        {
            var val = lvlParam.Value?.ToString();
            DynamicLogFilters.HttpMinLevel = ParseLogLevel(val, LogLevel.Warning);
        }

        // Apply Trigger worker options (OptionsMonitor will observe cache changes)
        var opts = new GameBot.Service.Hosted.TriggerWorkerOptions();
        opts.IntervalSeconds = GetInt(snapshot, "Service__Triggers__Worker__IntervalSeconds", 2);
        opts.GameFilter = GetString(snapshot, "Service__Triggers__Worker__GameFilter", null);
        opts.SkipWhenNoSessions = GetBool(snapshot, "Service__Triggers__Worker__SkipWhenNoSessions", true);
        opts.IdleBackoffSeconds = GetInt(snapshot, "Service__Triggers__Worker__IdleBackoffSeconds", 5);

        _triggerOptionsCache.TryRemove(Options.DefaultName);
        _triggerOptionsCache.TryAdd(Options.DefaultName, opts);
    }

    private static int GetInt(ConfigurationSnapshot snap, string key, int @default)
    {
        return snap.Parameters.TryGetValue(key, out var p) && p.Value is not null && int.TryParse(p.Value.ToString(), out var v)
            ? v : @default;
    }
    private static bool GetBool(ConfigurationSnapshot snap, string key, bool @default)
    {
        if (!snap.Parameters.TryGetValue(key, out var p) || p.Value is null) return @default;
        var s = p.Value.ToString();
        if (bool.TryParse(s, out var b)) return b;
        // Accept 0/1
        if (int.TryParse(s, out var i)) return i != 0;
        return @default;
    }
    private static string? GetString(ConfigurationSnapshot snap, string key, string? @default)
    {
        return snap.Parameters.TryGetValue(key, out var p) && p.Value is not null ? p.Value.ToString() : @default;
    }
}

internal sealed class NoopConfigApplier : IConfigApplier
{
    public void Apply(ConfigurationSnapshot snapshot) { }
}
