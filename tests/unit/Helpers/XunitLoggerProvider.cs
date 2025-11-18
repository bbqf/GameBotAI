using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace GameBot.UnitTests.Helpers;

internal sealed class TestLoggerProvider : ILoggerProvider
{
    private readonly LogLevel _minLevel;
    private readonly ConcurrentBag<LogEntry> _entries = new();

    public TestLoggerProvider(LogLevel minLevel = LogLevel.Information)
    {
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName) => new TestLogger(categoryName, _entries, _minLevel);

    public void Dispose() { }

    public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

    internal readonly record struct LogEntry(string Category, LogLevel Level, string Message);

    private sealed class TestLogger : ILogger
    {
        private readonly string _category;
        private readonly ConcurrentBag<LogEntry> _entries;
        private readonly LogLevel _minLevel;

        public TestLogger(string category, ConcurrentBag<LogEntry> entries, LogLevel minLevel)
        { _category = category; _entries = entries; _minLevel = minLevel; }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            _entries.Add(new LogEntry(_category, logLevel, msg));
            try { System.Console.WriteLine($"{logLevel} {_category}[{eventId.Id}] {msg}"); } catch { }
        }

        private sealed class NullScope : IDisposable { public static readonly NullScope Instance = new(); public void Dispose() { } }
    }
}
