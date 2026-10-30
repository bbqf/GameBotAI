using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.UnitTests.Parameters;

/// <summary>
/// Feature 169: the web UI keeps a copy of <see cref="CommandStepFieldPaths"/> in
/// <c>fieldTemplatePaths.ts</c>. This test fails when the copy and the C# class are different.
/// </summary>
public sealed class FieldTemplatePathMirrorTests {
  private static readonly Regex EntryPattern = new(
      @"\{\s*path:\s*'(?<path>[^']+)'\s*,\s*kind:\s*'(?<kind>integer|decimal|image)'\s*\}",
      RegexOptions.Compiled, TimeSpan.FromSeconds(1));

  private static string MirrorFile() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null) {
      var candidate = Path.Combine(dir.FullName, "src", "web-ui", "src", "components", "parameters", "fieldTemplatePaths.ts");
      if (File.Exists(candidate)) return candidate;
      dir = dir.Parent;
    }

    throw new FileNotFoundException("fieldTemplatePaths.ts was not found above the test folder.");
  }

  private static (string Path, string Kind)[] MirrorEntries() =>
      EntryPattern.Matches(File.ReadAllText(MirrorFile()))
          .Select(m => (m.Groups["path"].Value, m.Groups["kind"].Value))
          .OrderBy(e => e.Item1, StringComparer.Ordinal)
          .ToArray();

  private static (string Path, string Kind)[] ServiceEntries() =>
      CommandStepFieldPaths.SupportedNumericPaths
          .Select(p => (p.Key, p.Value ? "integer" : "decimal"))
          .Concat(CommandStepFieldPaths.SupportedImagePaths.Select(p => (p, "image")))
          .OrderBy(e => e.Item1, StringComparer.Ordinal)
          .ToArray();

  [Fact]
  public void MirrorHasTheSamePathsAndKindsAsTheService() {
    MirrorEntries().Should().Equal(ServiceEntries());
  }

  [Fact]
  public void MirrorHasNoDuplicatePath() {
    var paths = MirrorEntries().Select(e => e.Path).ToList();
    paths.Should().OnlyHaveUniqueItems();
  }
}
