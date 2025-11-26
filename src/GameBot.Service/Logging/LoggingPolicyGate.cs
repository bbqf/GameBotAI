using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GameBot.Domain.Logging;
using GameBot.Domain.Services.Logging;
using GameBot.Service.Services;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Logging;

/// <summary>
/// Applies component-level logging policy decisions to the ASP.NET Core logging pipeline.
/// </summary>
internal sealed class LoggingPolicyGate : ILoggingPolicyApplier
{
    private readonly object _updateLock = new();
    private ComponentGate[] _components = Array.Empty<ComponentGate>();
    private int _defaultLevel = (int)LogLevel.Warning;

    public bool ShouldLog(string? provider, string? category, LogLevel level)
    {
        if (level == LogLevel.None)
        {
            return false;
        }

        var effectiveCategory = string.IsNullOrEmpty(category) ? provider : category;

        if (!string.IsNullOrEmpty(effectiveCategory) && DynamicLogFilters.IsHttpCategory(effectiveCategory))
        {
            if (level < DynamicLogFilters.HttpMinLevel)
            {
                return false;
            }
        }

        var gates = Volatile.Read(ref _components);
        if (!string.IsNullOrEmpty(effectiveCategory) && gates.Length > 0)
        {
            foreach (var gate in gates)
            {
                if (effectiveCategory.StartsWith(gate.Name, StringComparison.Ordinal))
                {
                    if (!gate.Enabled)
                    {
                        return false;
                    }

                    return level >= gate.Level;
                }
            }
        }

        var fallback = (LogLevel)Volatile.Read(ref _defaultLevel);
        return level >= fallback;
    }

    public void ApplyComponent(LoggingComponentSetting component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (string.IsNullOrWhiteSpace(component.Name))
        {
            throw new ArgumentException("Component name is required", nameof(component));
        }

        lock (_updateLock)
        {
            var list = _components.Length == 0
                ? new List<ComponentGate>()
                : _components.ToList();

            var updatedGate = new ComponentGate(component.Name, component.Enabled, component.EffectiveLevel);
            var index = list.FindIndex(g => string.Equals(g.Name, component.Name, StringComparison.Ordinal));
            if (index >= 0)
            {
                list[index] = updatedGate;
            }
            else
            {
                list.Add(updatedGate);
            }

            Volatile.Write(ref _components, list
                .OrderByDescending(g => g.Name.Length)
                .ToArray());
        }
    }

    public void ApplyAll(IEnumerable<LoggingComponentSetting> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var materialized = components.ToList();

        lock (_updateLock)
        {
            Volatile.Write(ref _components, materialized
                .Where(c => !string.IsNullOrWhiteSpace(c.Name))
                .Select(c => new ComponentGate(c.Name, c.Enabled, c.EffectiveLevel))
                .OrderByDescending(g => g.Name.Length)
                .ToArray());

            var defaultLevel = materialized.Count > 0 ? materialized[0].DefaultLevel : LogLevel.Warning;
            Volatile.Write(ref _defaultLevel, (int)defaultLevel);
        }
    }

    private readonly record struct ComponentGate(string Name, bool Enabled, LogLevel Level);
}