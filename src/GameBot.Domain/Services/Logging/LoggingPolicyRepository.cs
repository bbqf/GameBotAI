using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Logging;
using Microsoft.Extensions.Logging;

namespace GameBot.Domain.Services.Logging;

public interface ILoggingPolicyRepository
{
    Task<LoggingPolicySnapshot> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(LoggingPolicySnapshot snapshot, CancellationToken ct = default);
}

/// <summary>
/// File-backed repository that persists the logging policy snapshot under <c>data/config/logging-policy.json</c>.
/// </summary>
public sealed partial class LoggingPolicyRepository : ILoggingPolicyRepository, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    static LoggingPolicyRepository()
    {
        JsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private readonly string _filePath;
    private readonly ILogger<LoggingPolicyRepository> _logger;
    private readonly Func<LoggingPolicySnapshot> _defaultFactory;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public LoggingPolicyRepository(string storageRoot, Func<LoggingPolicySnapshot> defaultFactory, ILogger<LoggingPolicyRepository> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        _defaultFactory = defaultFactory ?? throw new ArgumentNullException(nameof(defaultFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var configDir = Path.Combine(storageRoot, "config");
        Directory.CreateDirectory(configDir);
        _filePath = Path.Combine(configDir, "logging-policy.json");
    }

    public async Task<LoggingPolicySnapshot> LoadAsync(CancellationToken ct = default)
    {
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                Log.PolicyFileMissing(_logger, _filePath);
                return _defaultFactory();
            }

            using var fs = File.OpenRead(_filePath);
            var snapshot = await JsonSerializer.DeserializeAsync<LoggingPolicySnapshot>(fs, JsonOptions, ct).ConfigureAwait(false);
            if (snapshot is null)
            {
                Log.PolicyFileEmpty(_logger, _filePath);
                return _defaultFactory();
            }

            return snapshot;
        }
        catch (JsonException ex)
        {
            Log.PolicyFileInvalid(_logger, ex, _filePath);
            return _defaultFactory();
        }
        catch (IOException ex)
        {
            Log.PolicyFileIoFailure(_logger, ex, _filePath);
            return _defaultFactory();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task SaveAsync(LoggingPolicySnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _mutex.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public void Dispose()
    {
        _mutex.Dispose();
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Logging policy file not found at {Path}; returning default snapshot")]
        public static partial void PolicyFileMissing(ILogger logger, string path);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Logging policy file {Path} was empty; recreating default snapshot")]
        public static partial void PolicyFileEmpty(ILogger logger, string path);

        [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Failed to parse logging policy at {Path}. Returning default snapshot.")]
        public static partial void PolicyFileInvalid(ILogger logger, Exception exception, string path);

        [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Failed to load logging policy at {Path}. Returning default snapshot.")]
        public static partial void PolicyFileIoFailure(ILogger logger, Exception exception, string path);
    }
}