using System;
using FluentAssertions;
using GameBot.Service.Services.SequenceExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 068: the reschedule decision message (written to the execution log, FR-007/SC-004) must
/// unambiguously state whether the OCR-parsed value or the static fallback was used, with the read
/// text / reason and the resulting offset.
/// </summary>
public sealed class OcrOffsetMessageTests {
  [Fact]
  public void OcrSourceMessageIncludesRecognizedTextAndDuration() {
    var msg = SequenceExecutionService.DescribeOcrOffset(
      new OcrOffsetResolution(new TimeSpan(0, 5, 42), OcrOffsetSource.Ocr, "00:05:42", null));

    msg.Should().Contain("offset source ocr");
    msg.Should().Contain("00:05:42");
  }

  [Fact]
  public void FallbackSourceMessageIncludesReasonAndOffset() {
    var msg = SequenceExecutionService.DescribeOcrOffset(
      new OcrOffsetResolution(new TimeSpan(0, 6, 0), OcrOffsetSource.Fallback, null, "ocr-unavailable"));

    msg.Should().Contain("offset source fallback");
    msg.Should().Contain("ocr-unavailable");
    msg.Should().Contain("00:06:00");
  }

  [Fact]
  public void FallbackWithRecognizedTextAlsoReportsWhatWasRead() {
    var msg = SequenceExecutionService.DescribeOcrOffset(
      new OcrOffsetResolution(new TimeSpan(0, 6, 0), OcrOffsetSource.Fallback, "no timer", "parse-failed"));

    msg.Should().Contain("offset source fallback");
    msg.Should().Contain("parse-failed");
    msg.Should().Contain("no timer");
  }
}
