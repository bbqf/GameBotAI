using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Services;

/// <summary>The closed list of failure reason codes of an <c>ocrText</c> evaluation.</summary>
public static class OcrTextFailureReasons {
  /// <summary>The run has no emulator session, or no scope is present.</summary>
  public const string NoSession = "no-session";

  /// <summary>The frame capture failed or gave no frame.</summary>
  public const string CaptureFailed = "capture-failed";

  /// <summary>The OCR engine is not available.</summary>
  public const string OcrUnavailable = "ocr-unavailable";

  /// <summary>The region is not fully inside the frame.</summary>
  public const string RegionOutsideFrame = "region-outside-frame";

  /// <summary>The region has a width or height of zero or less.</summary>
  public const string RegionInvalid = "region-invalid";

  /// <summary>The OCR engine failed during the read.</summary>
  public const string OcrError = "ocr-error";

  /// <summary>A parser is named and the text does not parse, or the parser name is unknown.</summary>
  public const string ParseFailed = "parse-failed";
}

/// <summary>The result of one screen read for an <c>ocrText</c> condition.</summary>
/// <param name="Text">The raw text, or null on failure.</param>
/// <param name="Parsed">The parsed text, or null. Set only when a parser is named and the text parses.</param>
/// <param name="FailureReason">One code from <see cref="OcrTextFailureReasons"/>. Null on success.</param>
public sealed record OcrTextReadResult(string? Text, string? Parsed, string? FailureReason);

/// <summary>One record for each evaluation of an <c>ocrText</c> leaf. It is for the run log only.</summary>
public sealed record OcrTextEvaluationRecord(
    string? TextRead,
    string? Compared,
    string? Expected,
    string? Pattern,
    string? Parser,
    bool Result,
    string? Reason,
    string? Note) {
  /// <summary>The note after a <c>pattern</c> time-out.</summary>
  public const string PatternTimedOutNote = "pattern timed out";

  /// <summary>Formats the log line of this record.</summary>
  public string ToLogLine() {
    var criterion = Pattern is not null
      ? string.Format(CultureInfo.InvariantCulture, "pattern='{0}'", Pattern)
      : string.Format(CultureInfo.InvariantCulture, "expected='{0}'", Expected);
    var line = string.Format(
      CultureInfo.InvariantCulture,
      "ocrText: read='{0}' {1} result={2}",
      TextRead ?? string.Empty,
      criterion,
      Result ? "true" : "false");
    if (Reason is not null) line += " reason=" + Reason;
    if (Note is not null) line += " " + Note;
    return line;
  }
}

/// <summary>
/// The screen reader and the records of the current run, made ambient for the duration of the run. An
/// <c>ocrText</c> condition needs the screen, but the runner does not get a reader as a parameter: a new
/// parameter through each private method of <see cref="SequenceRunner"/> would make its large methods
/// larger. The same pattern as <see cref="SequenceRunContext"/>.
/// <para>
/// A run entry point that has an emulator session pushes a scope. A run with no session has no scope, so
/// the condition is false with the reason <c>no-session</c>.
/// </para>
/// </summary>
public sealed class OcrTextEvaluationScope {
  private static readonly AsyncLocal<OcrTextEvaluationScope?> Ambient = new();
  private readonly List<OcrTextEvaluationRecord> _records = new();
  private readonly object _gate = new();

  /// <summary>Creates a scope.</summary>
  /// <param name="reader">
  /// Reads the region of the condition from the screen of the run. Null for a run with no session: the
  /// scope then only keeps the records, and the condition is false with the reason <c>no-session</c>.
  /// </param>
  public OcrTextEvaluationScope(Func<OcrTextStepCondition, CancellationToken, Task<OcrTextReadResult>>? reader) {
    Reader = reader;
  }

  /// <summary>Reads the region of a condition from the screen of the run. Null when the run has no session.</summary>
  public Func<OcrTextStepCondition, CancellationToken, Task<OcrTextReadResult>>? Reader { get; }

  /// <summary>
  /// Makes a scope with no reader current when no scope is current, so the run log can show the reason
  /// <c>no-session</c>. Returns null when a scope is already current.
  /// </summary>
  public static IDisposable? EnsureScope() => Ambient.Value is null ? Push(new OcrTextEvaluationScope(null)) : null;

  /// <summary>The scope of the current async flow, or null.</summary>
  public static OcrTextEvaluationScope? Current => Ambient.Value;

  /// <summary>Adds a record.</summary>
  public void Add(OcrTextEvaluationRecord record) {
    ArgumentNullException.ThrowIfNull(record);
    lock (_gate) _records.Add(record);
  }

  /// <summary>Returns the records added since the last drain and clears the list.</summary>
  public IReadOnlyList<OcrTextEvaluationRecord> Drain() {
    lock (_gate) {
      var copy = _records.ToArray();
      _records.Clear();
      return copy;
    }
  }

  /// <summary>
  /// Drains the records of the current scope and returns one log line for each, joined with "; ".
  /// Returns an empty text when no scope or no record is present.
  /// </summary>
  public static string DrainLog() {
    var scope = Current;
    if (scope is null) return string.Empty;
    var records = scope.Drain();
    if (records.Count == 0) return string.Empty;
    var lines = new string[records.Count];
    for (var i = 0; i < records.Count; i++) lines[i] = records[i].ToLogLine();
    return string.Join("; ", lines);
  }

  /// <summary>Makes <paramref name="scope"/> current until the returned handle is disposed.</summary>
  public static IDisposable Push(OcrTextEvaluationScope scope) {
    ArgumentNullException.ThrowIfNull(scope);
    var previous = Ambient.Value;
    Ambient.Value = scope;
    return new Restore(previous);
  }

  private sealed class Restore : IDisposable {
    private readonly OcrTextEvaluationScope? _previous;
    private bool _disposed;

    public Restore(OcrTextEvaluationScope? previous) => _previous = previous;

    public void Dispose() {
      if (_disposed) return;
      _disposed = true;
      Ambient.Value = _previous;
    }
  }
}
