using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Logging;
using Microsoft.Extensions.Logging;

namespace GameBot.Domain.Services.Logging;

public interface ILoggingPolicyApplier
{
    void ApplyComponent(LoggingComponentSetting component);
    void ApplyAll(IEnumerable<LoggingComponentSetting> components);
}

public sealed class NullLoggingPolicyApplier : ILoggingPolicyApplier
{
    public static readonly NullLoggingPolicyApplier Instance = new();

    private NullLoggingPolicyApplier() { }

    public void ApplyComponent(LoggingComponentSetting component) { }

    public void ApplyAll(IEnumerable<LoggingComponentSetting> components) { }
}

public interface IRuntimeLoggingPolicyService
{
    event EventHandler<LoggingChangeAuditEventArgs>? AuditRecorded;

    Task<LoggingPolicySnapshot> GetSnapshotAsync(CancellationToken ct = default);
    Task<LoggingComponentSetting> SetComponentAsync(string componentName, LogLevel? level, bool? enabled, string actor, string? notes, CancellationToken ct = default);
    Task<LoggingPolicySnapshot> ResetAsync(string actor, string? reason, CancellationToken ct = default);
}

public sealed partial class RuntimeLoggingPolicyService : IRuntimeLoggingPolicyService, IDisposable
{
    private readonly ILoggingPolicyRepository _repository;
    private readonly ILogger<RuntimeLoggingPolicyService> _logger;
    private readonly ILoggingPolicyApplier _applier;
    private readonly List<string> _componentCatalog;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private LoggingPolicySnapshot? _snapshot;

    public RuntimeLoggingPolicyService(
        ILoggingPolicyRepository repository,
        IEnumerable<string> componentCatalog,
        ILogger<RuntimeLoggingPolicyService> logger,
        ILoggingPolicyApplier? applier = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _applier = applier ?? NullLoggingPolicyApplier.Instance;
        _componentCatalog = componentCatalog?.Distinct(StringComparer.Ordinal).ToList()
            ?? throw new ArgumentNullException(nameof(componentCatalog));
        if (_componentCatalog.Count == 0)
        {
            throw new ArgumentException("Component catalog must contain at least one entry", nameof(componentCatalog));
        }
    }

    public event EventHandler<LoggingChangeAuditEventArgs>? AuditRecorded;

    public void Dispose()
    {
        _mutex.Dispose();
    }

    public async Task<LoggingPolicySnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = await EnsureSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.DeepClone();
    }

    public async Task<LoggingComponentSetting> SetComponentAsync(string componentName, LogLevel? level, bool? enabled, string actor, string? notes, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(componentName)) throw new ArgumentException("Component name is required", nameof(componentName));
        if (level is null && enabled is null)
        {
            throw new ArgumentException("Either level or enabled flag must be provided", nameof(level));
        }

        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await EnsureSnapshotInternalAsync(ct).ConfigureAwait(false);
            var components = snapshot.Components.ToList();
            var index = components.FindIndex(c => string.Equals(c.Name, componentName, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new KeyNotFoundException($"Component '{componentName}' is not recognized.");
            }

            var current = components[index];
            var newLevel = level ?? current.EffectiveLevel;
            var newEnabled = enabled ?? current.Enabled;
            if (newLevel == current.EffectiveLevel && newEnabled == current.Enabled)
            {
                return current;
            }

            var now = DateTimeOffset.UtcNow;
            var updated = current with
            {
                EffectiveLevel = newLevel,
                Enabled = newEnabled,
                LastChangedAtUtc = now,
                LastChangedBy = actor,
                Source = "api",
                Notes = notes
            };

            components[index] = updated;
            var updatedSnapshot = snapshot with
            {
                Components = components,
                AppliedAtUtc = now,
                AppliedBy = actor,
                AllEnabled = components.All(c => c.Enabled)
            };

            await _repository.SaveAsync(updatedSnapshot, ct).ConfigureAwait(false);
            _snapshot = updatedSnapshot;
            _applier.ApplyComponent(updated);
            EmitAudit(current, updated, actor, DetermineAction(level, enabled), notes, now);
            return updated;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<LoggingPolicySnapshot> ResetAsync(string actor, string? reason, CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snapshot = await EnsureSnapshotInternalAsync(ct).ConfigureAwait(false);
            var now = DateTimeOffset.UtcNow;
            var components = snapshot.Components
                .Select(c => c with
                {
                    Enabled = true,
                    EffectiveLevel = c.DefaultLevel,
                    LastChangedAtUtc = now,
                    LastChangedBy = actor,
                    Source = "reset",
                    Notes = reason
                })
                .ToList();

            var updatedSnapshot = snapshot with
            {
                Components = components,
                AppliedAtUtc = now,
                AppliedBy = actor,
                AllEnabled = true
            };

            await _repository.SaveAsync(updatedSnapshot, ct).ConfigureAwait(false);
            _snapshot = updatedSnapshot;
            _applier.ApplyAll(components);
            EmitAudit(null, null, actor, "reset", reason, now);
            return updatedSnapshot.DeepClone();
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<LoggingPolicySnapshot> EnsureSnapshotAsync(CancellationToken ct)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return (await EnsureSnapshotInternalAsync(ct).ConfigureAwait(false)).DeepClone();
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task<LoggingPolicySnapshot> EnsureSnapshotInternalAsync(CancellationToken ct)
    {
        if (_snapshot is not null)
        {
            return _snapshot;
        }

        var loaded = await _repository.LoadAsync(ct).ConfigureAwait(false);
        var ensured = EnsureCatalogEntries(loaded);
        _snapshot = ensured;
        _applier.ApplyAll(ensured.Components);
        return ensured;
    }

    private LoggingPolicySnapshot EnsureCatalogEntries(LoggingPolicySnapshot snapshot)
    {
        var list = snapshot.Components.ToList();
        var existing = new HashSet<string>(list.Select(c => c.Name), StringComparer.Ordinal);
        var changed = false;
        foreach (var name in _componentCatalog)
        {
            if (existing.Contains(name))
            {
                continue;
            }
            list.Add(LoggingComponentSetting.CreateDefault(name, snapshot.DefaultLevel));
            changed = true;
        }

        if (!changed)
        {
            return snapshot;
        }

        return snapshot with { Components = list, AllEnabled = list.All(c => c.Enabled) };
    }

    private void EmitAudit(LoggingComponentSetting? before, LoggingComponentSetting? after, string actor, string action, string? reason, DateTimeOffset occurredAt)
    {
        var audit = new LoggingChangeAudit
        {
            Component = after?.Name ?? before?.Name ?? "*",
            Action = action,
            Actor = actor,
            OccurredAtUtc = occurredAt,
            Reason = reason,
            Before = before,
            After = after
        };

        Log.LoggingPolicyChange(_logger, action, audit.Component, actor);
        AuditRecorded?.Invoke(this, new LoggingChangeAuditEventArgs(audit));
    }

    private static string DetermineAction(LogLevel? level, bool? enabled)
    {
        if (level.HasValue && enabled.HasValue)
        {
            return "set-component";
        }

        if (level.HasValue)
        {
            return "set-level";
        }

        return "toggle-enabled";
    }
    private static partial class Log
    {
        [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Logging policy change {Action} for {Component} by {Actor}")]
        public static partial void LoggingPolicyChange(ILogger logger, string action, string component, string actor);
    }
}