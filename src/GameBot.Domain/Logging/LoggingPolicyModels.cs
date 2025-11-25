using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace GameBot.Domain.Logging;

/// <summary>
/// Represents the persisted and runtime state for an individual logging component/category.
/// </summary>
public sealed record class LoggingComponentSetting
{
    public string Name { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public LogLevel EffectiveLevel { get; init; } = LogLevel.Warning;
    public LogLevel DefaultLevel { get; init; } = LogLevel.Warning;
    public string Source { get; init; } = "default";
    public string? LastChangedBy { get; init; }
    public DateTimeOffset? LastChangedAtUtc { get; init; }
    public string? Notes { get; init; }

    public static LoggingComponentSetting CreateDefault(string name, LogLevel defaultLevel) => new()
    {
        Name = name,
        Enabled = true,
        EffectiveLevel = defaultLevel,
        DefaultLevel = defaultLevel,
        Source = "default"
    };
}

/// <summary>
/// Captures the full logging policy snapshot that is persisted to disk and surfaced via the config API.
/// </summary>
public sealed record class LoggingPolicySnapshot
{
    public string Id { get; init; } = "logging-policy";
    public string Version { get; init; } = "1.0";
    public LogLevel DefaultLevel { get; init; } = LogLevel.Warning;
    public bool AllEnabled { get; init; } = true;
    public DateTimeOffset AppliedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? AppliedBy { get; init; }
    public IReadOnlyList<LoggingComponentSetting> Components { get; init; } = Array.Empty<LoggingComponentSetting>();

    public static LoggingPolicySnapshot CreateDefault(IEnumerable<string> componentNames, LogLevel defaultLevel, string? appliedBy)
    {
        var list = componentNames
            .Distinct(StringComparer.Ordinal)
            .Select(name => LoggingComponentSetting.CreateDefault(name, defaultLevel))
            .ToList();

        return new LoggingPolicySnapshot
        {
            Id = "logging-policy",
            Version = "1.0",
            DefaultLevel = defaultLevel,
            AllEnabled = true,
            AppliedAtUtc = DateTimeOffset.UtcNow,
            AppliedBy = appliedBy,
            Components = list
        };
    }

    public LoggingPolicySnapshot DeepClone()
    {
        var clonedComponents = Components.Select(c => c with { }).ToList();
        return this with { Components = clonedComponents };
    }
}

/// <summary>
/// Audit entry emitted whenever the logging policy changes.
/// </summary>
public sealed record class LoggingChangeAudit
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Component { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Actor { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public string? Reason { get; init; }
    public LoggingComponentSetting? Before { get; init; }
    public LoggingComponentSetting? After { get; init; }
}

public sealed class LoggingChangeAuditEventArgs : EventArgs
{
    public LoggingChangeAuditEventArgs(LoggingChangeAudit audit)
    {
        Audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public LoggingChangeAudit Audit { get; }
}