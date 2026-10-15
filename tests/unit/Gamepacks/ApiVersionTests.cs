using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 144: the constants and the docs line.</summary>
public sealed class ApiVersionTests {
  [Fact]
  public void TheConstantsHaveTheExpectedValues() {
    ApiVersion.Major.Should().Be(1);
    ApiVersion.Minor.Should().Be(0);
    ApiVersion.Patch.Should().Be(0);
    ApiVersion.SchemaFormat.Should().Be(1);
  }

  [Fact]
  public void TheDocsLineEqualsTheConstants() {
    var path = FindDocs();
    var expected = $"API version: {ApiVersion.Major}.{ApiVersion.Minor}.{ApiVersion.Patch}, schema format {ApiVersion.SchemaFormat}";

    File.ReadAllLines(path).Count(l => l.Trim() == expected).Should().Be(1);
  }

  private static string FindDocs() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null) {
      var candidate = Path.Combine(dir.FullName, "docs", "gamepacks.md");
      if (File.Exists(candidate)) {
        return candidate;
      }

      dir = dir.Parent;
    }

    throw new FileNotFoundException("docs/gamepacks.md not found above " + AppContext.BaseDirectory);
  }
}
