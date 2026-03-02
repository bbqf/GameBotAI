using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

public sealed class ExecutionLogSanitizerTests {
  [Fact]
  public void SanitizeDetailsRedactsSensitiveKeys() {
    var details = new[]
    {
      new ExecutionDetailItem(
        "step",
        "detail",
        new Dictionary<string, object?>
        {
          ["token"] = "abc",
          ["apiKey"] = "xyz",
          ["other"] = "value"
        },
        "normal")
    };

    var sanitized = ExecutionLogSanitizer.SanitizeDetails(details);

    sanitized.Should().HaveCount(1);
    sanitized[0].Attributes.Should().NotBeNull();
    sanitized[0].Attributes!["token"].Should().Be("[REDACTED]");
    sanitized[0].Attributes!["apiKey"].Should().Be("[REDACTED]");
    sanitized[0].Attributes!["other"].Should().Be("value");
    sanitized[0].Sensitivity.Should().Be("redacted");
  }

  [Fact]
  public void SanitizeDetailsKeepsSensitivityWhenNoSensitiveKeys() {
    var details = new[]
    {
      new ExecutionDetailItem(
        "step",
        "detail",
        new Dictionary<string, object?>
        {
          ["x"] = 10,
          ["reason"] = "not_found"
        },
        "normal")
    };

    var sanitized = ExecutionLogSanitizer.SanitizeDetails(details);

    sanitized[0].Attributes!["x"].Should().Be(10);
    sanitized[0].Attributes!["reason"].Should().Be("not_found");
    sanitized[0].Sensitivity.Should().Be("normal");
  }
}
