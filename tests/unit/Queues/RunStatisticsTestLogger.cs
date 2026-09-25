using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// Feature 105 tests: a logger that keeps each entry, so that a test can count the Warning entries.
/// </summary>
internal sealed class RunStatisticsTestLogger<T> : ILogger<T> {
  public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

  public int WarningCount => Entries.Count(e => e.Level == LogLevel.Warning);

  public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

  public bool IsEnabled(LogLevel logLevel) => true;

  public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
    ArgumentNullException.ThrowIfNull(formatter);
    Entries.Enqueue((logLevel, formatter(state, exception)));
  }
}
