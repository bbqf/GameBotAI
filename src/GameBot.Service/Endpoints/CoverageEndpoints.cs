using System;
using System.Collections.Generic;
using GameBot.Service.Models;
using GameBot.Service.Services.Ocr;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameBot.Service.Endpoints;

internal static class CoverageEndpoints {
  public static IEndpointRouteBuilder MapCoverageEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup("/api/ocr");

    group.MapGet("coverage", async (ICoverageSummaryService service, CancellationToken ct) => {
      var result = await service.GetLatestAsync(ct).ConfigureAwait(false);
      return result.Status switch {
        CoverageSummaryStatus.Success => Results.Ok(ToResponse(result.Summary!)),
        CoverageSummaryStatus.Stale => BuildUnavailable("Coverage summary is stale. Re-run tools/coverage/report.ps1.", result.Summary?.GeneratedAtUtc),
        _ => BuildUnavailable("Coverage summary missing. Run tools/coverage/report.ps1 before calling the endpoint.")
      };
    })
    .WithName("GetOcrCoverageSummary");

    return app;
  }

  private static OcrCoverageSummaryResponse ToResponse(OcrCoverageSummary summary) => new() {
    GeneratedAtUtc = summary.GeneratedAtUtc,
    Namespace = summary.Namespace,
    LineCoveragePercent = summary.LineCoveragePercent,
    TargetPercent = summary.TargetPercent,
    Passed = summary.Passed,
    UncoveredScenarios = summary.UncoveredScenarios.Count > 0 ? summary.UncoveredScenarios : null,
    ReportUrl = summary.ReportUrl
  };

  private static IResult BuildUnavailable(string message, DateTime? generatedAtUtc = null) {
    var payload = new Dictionary<string, object?> { ["error"] = message };
    if (generatedAtUtc.HasValue) {
      payload["details"] = new { generatedAtUtc };
    }
    return Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
  }
}
