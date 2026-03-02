using System.Text.Json;

namespace GameBot.Domain.Logging;

public interface IExecutionLogRetentionPolicyRepository
{
  Task<ExecutionLogRetentionPolicy> GetAsync(CancellationToken ct = default);
  Task<ExecutionLogRetentionPolicy> SaveAsync(ExecutionLogRetentionPolicy policy, CancellationToken ct = default);
}

public sealed class ExecutionLogRetentionPolicyRepository : IExecutionLogRetentionPolicyRepository, IDisposable
{
  private readonly string _filePath;
  private readonly SemaphoreSlim _mutex = new(1, 1);
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true
  };

  public ExecutionLogRetentionPolicyRepository(string storageRoot)
  {
    var configDir = Path.Combine(storageRoot, "config");
    Directory.CreateDirectory(configDir);
    _filePath = Path.Combine(configDir, "execution-log-policy.json");
  }

  public async Task<ExecutionLogRetentionPolicy> GetAsync(CancellationToken ct = default)
  {
    await _mutex.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      if (!File.Exists(_filePath))
      {
        return ExecutionLogRetentionPolicy.Default;
      }

      using var fs = File.OpenRead(_filePath);
      var item = await JsonSerializer.DeserializeAsync<ExecutionLogRetentionPolicy>(fs, JsonOptions, ct).ConfigureAwait(false);
      return item ?? ExecutionLogRetentionPolicy.Default;
    }
    catch
    {
      return ExecutionLogRetentionPolicy.Default;
    }
    finally
    {
      _mutex.Release();
    }
  }

  public async Task<ExecutionLogRetentionPolicy> SaveAsync(ExecutionLogRetentionPolicy policy, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(policy);
    await _mutex.WaitAsync(ct).ConfigureAwait(false);
    try
    {
      var persisted = new ExecutionLogRetentionPolicy
      {
        Enabled = policy.Enabled,
        RetentionDays = Math.Max(1, policy.RetentionDays),
        CleanupIntervalMinutes = Math.Max(1, policy.CleanupIntervalMinutes),
        UpdatedAtUtc = DateTimeOffset.UtcNow
      };
      using var fs = File.Create(_filePath);
      await JsonSerializer.SerializeAsync(fs, persisted, JsonOptions, ct).ConfigureAwait(false);
      return persisted;
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
}
