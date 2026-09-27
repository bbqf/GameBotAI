using System.Globalization;
using GameBot.Domain.Sessions;

namespace GameBot.Service.Services.Liveness;

/// <summary>The values of the three staleness headers (feature 106).</summary>
/// <param name="AgeMs">Milliseconds since the frame was captured.</param>
/// <param name="UnchangedMs">Milliseconds since the frame bytes last changed.</param>
/// <param name="Stale">The stale flag of the liveness report.</param>
internal readonly record struct CaptureHeaderValues(long AgeMs, long UnchangedMs, bool Stale);

/// <summary>
/// The screenshot and snapshot response headers (feature 106, contract <c>screenshot-snapshot.md</c>).
/// </summary>
internal static class CaptureHeaders {
  /// <summary>The ID of the stored capture (screenshot endpoint only; not new).</summary>
  public const string CaptureId = "X-Capture-Id";

  /// <summary>Milliseconds since the frame was captured. <c>0</c> for a direct capture.</summary>
  public const string AgeMs = "X-Capture-Age-Ms";

  /// <summary>Milliseconds since the frame bytes last changed.</summary>
  public const string UnchangedMs = "X-Capture-Unchanged-Ms";

  /// <summary><c>true</c> or <c>false</c>: the stale flag of the session liveness report.</summary>
  public const string Stale = "X-Capture-Stale";

  /// <summary>The headers that browser clients can read (CORS <c>Access-Control-Expose-Headers</c>).</summary>
  public static readonly string[] ExposedHeaders = { CaptureId, AgeMs, UnchangedMs, Stale };

  /// <summary>Writes the three staleness headers, as invariant-culture integers and lower-case booleans.</summary>
  public static void Apply(HttpResponse response, long ageMs, long unchangedMs, bool stale) {
    ArgumentNullException.ThrowIfNull(response);
    response.Headers[AgeMs] = ageMs.ToString(CultureInfo.InvariantCulture);
    response.Headers[UnchangedMs] = unchangedMs.ToString(CultureInfo.InvariantCulture);
    response.Headers[Stale] = stale ? "true" : "false";
  }

  /// <summary>Writes the header values of <paramref name="values"/>.</summary>
  public static void Apply(HttpResponse response, CaptureHeaderValues values) =>
    Apply(response, values.AgeMs, values.UnchangedMs, values.Stale);

  /// <summary>
  /// The header values for a frame. A cached frame of the capture loop gives the age, the unchanged time
  /// and the stale flag of the report. A direct capture gives the age <c>0</c>. With no capture-loop
  /// data, the unchanged time is <c>0</c> and stale is <c>false</c>, because the frame is new and the
  /// device answered in time.
  /// </summary>
  public static CaptureHeaderValues FromReport(DeviceLivenessReport report, bool directCapture) {
    ArgumentNullException.ThrowIfNull(report);
    var hasLoopData = report.UnchangedMs is not null;
    var age = directCapture ? 0 : report.FrameAgeMs ?? 0;
    var unchanged = hasLoopData ? report.UnchangedMs!.Value : 0;
    var stale = hasLoopData && report.Stale;
    return new CaptureHeaderValues(age, unchanged, stale);
  }
}
