using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using GameBot.Domain.Triggers.Evaluators;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Logging;

/// <summary>
/// Emits structured debug logs for every Tesseract CLI invocation with sanitized payloads.
/// </summary>
internal sealed class TesseractInvocationLogger : ITesseractInvocationLogger {
  private const string TruncationSuffix = "...<truncated>";
  private static readonly IReadOnlyList<string> EmptyArgs = Array.Empty<string>();
  private static readonly IReadOnlyDictionary<string, string?> EmptyEnv = new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>());
  private static readonly string[] SecretMarkers = { "TOKEN", "SECRET", "PASSWORD", "KEY" };

  private readonly ILogger<TesseractInvocationLogger> _logger;
  private static readonly Action<ILogger, Guid, string, string, string, string, bool, Exception?> LogInvocationMessage =
      LoggerMessage.Define<Guid, string, string, string, string, bool>(
          LogLevel.Debug,
          new EventId(5200, nameof(TesseractInvocationLogger)),
          "tesseract_invocation invocationId={InvocationId} exe={ExePath} args={Args} context={Context} streams={Streams} truncated={Truncated}");

  public TesseractInvocationLogger(ILogger<TesseractInvocationLogger> logger) {
    _logger = logger;
  }

  public void Log(in TesseractInvocationContext context) {
    if (!_logger.IsEnabled(LogLevel.Debug)) {
      return;
    }

    var entry = CreateLogEntry(context);
    var argsString = entry.Arguments.Count == 0 ? string.Empty : string.Join(' ', entry.Arguments);
    var envString = entry.EnvironmentOverrides.Count == 0
        ? string.Empty
        : string.Join("; ", entry.EnvironmentOverrides.Select(kvp => $"{kvp.Key}={kvp.Value}"));
    var contextString = $"workDir={entry.WorkingDirectory ?? string.Empty} env={envString} exit={entry.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? string.Empty} durationMs={entry.Duration.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)}";
    var streamsString = $"stdout={entry.StdOut ?? string.Empty} stderr={entry.StdErr ?? string.Empty}";

    LogInvocationMessage(_logger, entry.InvocationId, entry.ExePath, argsString, contextString, streamsString, entry.WasTruncated, null);
  }

  internal static TesseractInvocationLogEntry CreateLogEntry(in TesseractInvocationContext context) {
    var sanitizedArgs = SanitizeArguments(context.Arguments);
    var sanitizedEnv = SanitizeEnvironment(context.EnvironmentOverrides);
    var stdout = FormatStream(context.StdOut);
    var stderr = FormatStream(context.StdErr);
    var truncated = context.StdOut.WasTruncated || context.StdErr.WasTruncated;

    return new TesseractInvocationLogEntry(
        context.InvocationId,
        context.ExePath,
        sanitizedArgs,
        context.WorkingDirectory,
        sanitizedEnv,
        context.StartedAtUtc,
        context.CompletedAtUtc,
        context.CompletedAtUtc - context.StartedAtUtc,
        context.ExitCode,
        stdout,
        stderr,
        truncated);
  }

  private static IReadOnlyList<string> SanitizeArguments(IReadOnlyList<string> arguments) {
    if (arguments is null || arguments.Count == 0) {
      return EmptyArgs;
    }

    var sanitized = new List<string>(arguments.Count);
    var redactNext = false;
    foreach (var arg in arguments) {
      if (redactNext) {
        sanitized.Add("***");
        redactNext = false;
        continue;
      }

      if (string.IsNullOrEmpty(arg)) {
        sanitized.Add(arg);
        continue;
      }

      if (ContainsSecretMarker(arg)) {
        var equalsIndex = arg.IndexOf('=', StringComparison.Ordinal);
        if (equalsIndex >= 0) {
          sanitized.Add(arg[..(equalsIndex + 1)] + "***");
        }
        else {
          sanitized.Add($"{arg} ***");
          redactNext = true;
        }
      }
      else {
        sanitized.Add(arg);
      }
    }

    if (redactNext) {
      sanitized.Add("***");
    }

    return sanitized.AsReadOnly();
  }

  private static IReadOnlyDictionary<string, string?> SanitizeEnvironment(IReadOnlyDictionary<string, string?> env) {
    if (env is null || env.Count == 0) {
      return EmptyEnv;
    }

    var sanitized = new Dictionary<string, string?>(env.Count, StringComparer.OrdinalIgnoreCase);
    foreach (var kvp in env) {
      sanitized[kvp.Key] = ContainsSecretMarker(kvp.Key) ? "***" : kvp.Value;
    }

    return new ReadOnlyDictionary<string, string?>(sanitized);
  }

  private static bool ContainsSecretMarker(string? text) {
    if (string.IsNullOrEmpty(text)) {
      return false;
    }

    foreach (var marker in SecretMarkers) {
      if (text.Contains(marker, StringComparison.OrdinalIgnoreCase)) {
        return true;
      }
    }

    return false;
  }

  private static string? FormatStream(TesseractInvocationCapture stream) {
    if (string.IsNullOrEmpty(stream.Content)) {
      return stream.WasTruncated ? TruncationSuffix : null;
    }

    return stream.WasTruncated ? stream.Content + TruncationSuffix : stream.Content;
  }
}

internal sealed record class TesseractInvocationLogEntry(
    Guid InvocationId,
    string ExePath,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    IReadOnlyDictionary<string, string?> EnvironmentOverrides,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    TimeSpan Duration,
    int? ExitCode,
    string? StdOut,
    string? StdErr,
    bool WasTruncated);
