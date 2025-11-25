using System;
using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Service.Logging;
using Xunit;

namespace GameBot.UnitTests.TextOcr;

public class TesseractInvocationLoggerTests {
  [Fact]
  public void CreateLogEntryRedactsSecretArgumentsAndEnvironment() {
    var context = new TesseractInvocationContext(
        InvocationId: Guid.Parse("9d9f8eef-07f0-4f3e-a4ca-8dea19bbba60"),
        ExePath: "tesseract",
        Arguments: new List<string> { "--api-key=s3cr3t", "--plain=value", "--TOKEN", "naked" },
        WorkingDirectory: "C:/tmp",
        EnvironmentOverrides: new Dictionary<string, string?> {
          ["GAMEBOT_API_KEY"] = "abc",
          ["PATH"] = "safe"
        },
        StartedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
        CompletedAtUtc: DateTimeOffset.UtcNow,
        ExitCode: 0,
        StdOut: new TesseractInvocationCapture("ok", false),
        StdErr: new TesseractInvocationCapture(string.Empty, false)
    );

    var entry = TesseractInvocationLogger.CreateLogEntry(context);

    entry.Arguments.Should().Contain("--api-key=***");
    entry.Arguments.Should().Contain("--plain=value");
    entry.Arguments.Should().Contain("--TOKEN ***");
    entry.Arguments.Should().NotContain("naked");
    entry.EnvironmentOverrides.Should().ContainKey("GAMEBOT_API_KEY");
    entry.EnvironmentOverrides["GAMEBOT_API_KEY"].Should().Be("***");
    entry.EnvironmentOverrides["PATH"].Should().Be("safe");
  }

  [Fact]
  public void CreateLogEntryFlagsTruncationWhenStreamsOverLimit() {
    var context = new TesseractInvocationContext(
        InvocationId: Guid.NewGuid(),
        ExePath: "tesseract",
        Arguments: Array.Empty<string>(),
        WorkingDirectory: null,
        EnvironmentOverrides: new Dictionary<string, string?>(),
        StartedAtUtc: DateTimeOffset.UtcNow.AddMilliseconds(-5),
        CompletedAtUtc: DateTimeOffset.UtcNow,
        ExitCode: 1,
        StdOut: new TesseractInvocationCapture("a", false),
        StdErr: new TesseractInvocationCapture("error", true)
    );

    var entry = TesseractInvocationLogger.CreateLogEntry(context);

    entry.WasTruncated.Should().BeTrue();
    entry.StdErr.Should().EndWith("...<truncated>");
  }
}
