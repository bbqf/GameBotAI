using System.Globalization;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.Liveness;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace GameBot.UnitTests.Liveness;

/// <summary>Feature 106 (FR-010, contract <c>screenshot-snapshot.md</c>): the values of the capture headers.</summary>
public sealed class CaptureHeadersTests {
  private static DeviceLivenessReport Report(long? age, long? unchanged, bool stale) =>
    new(DeviceLivenessStates.Live, null, age, unchanged, stale, null, null, false);

  [Fact]
  public void ACachedFrameGivesTheReportValues() {
    var values = CaptureHeaders.FromReport(Report(312, 845210, true), directCapture: false);

    values.Should().Be(new CaptureHeaderValues(312, 845210, true));
  }

  [Fact]
  public void ADirectCaptureGivesAgeZero() {
    var values = CaptureHeaders.FromReport(Report(5000, 7000, true), directCapture: true);

    values.AgeMs.Should().Be(0);
    values.UnchangedMs.Should().Be(7000);
    values.Stale.Should().BeTrue();
  }

  [Fact]
  public void NoLoopDataGivesUnchangedZeroAndNotStale() {
    var values = CaptureHeaders.FromReport(Report(null, null, false), directCapture: true);

    values.Should().Be(new CaptureHeaderValues(0, 0, false));
  }

  [Fact]
  public void ApplyWritesInvariantIntegersAndLowerCaseBooleans() {
    var previous = CultureInfo.CurrentCulture;
    try {
      // A culture with a group separator must not change the header text.
      CultureInfo.CurrentCulture = new CultureInfo("de-DE");
      var context = new DefaultHttpContext();

      CaptureHeaders.Apply(context.Response, 1234567, 89, stale: true);

      context.Response.Headers[CaptureHeaders.AgeMs].ToString().Should().Be("1234567");
      context.Response.Headers[CaptureHeaders.UnchangedMs].ToString().Should().Be("89");
      context.Response.Headers[CaptureHeaders.Stale].ToString().Should().Be("true");

      CaptureHeaders.Apply(context.Response, 0, 0, stale: false);
      context.Response.Headers[CaptureHeaders.Stale].ToString().Should().Be("false");
    }
    finally {
      CultureInfo.CurrentCulture = previous;
    }
  }

  [Fact]
  public void TheExposedHeadersAreTheFourNames() {
    CaptureHeaders.ExposedHeaders.Should().Equal("X-Capture-Id", "X-Capture-Age-Ms", "X-Capture-Unchanged-Ms", "X-Capture-Stale");
  }
}
