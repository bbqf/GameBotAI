using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Ci;

public class TestShardTests {
  private static readonly Regex NamespaceRegex = new(@"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
  private static readonly Regex ClassRegex = new(@"^\s*(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+)*class\s+(\w+)", RegexOptions.Multiline);
  private static readonly Regex TestAttributeRegex = new(@"\[(Fact|Theory)\b");

  // Finds the fully qualified names of the test classes of a test project.
  // The source files are scanned, so the test does not need the other test assemblies.
  private static List<string> FindTestClasses(string folder) {
    var root = Path.Combine(CiScriptRunner.FindRepoRoot(), "tests", folder);
    var result = new List<string>();
    foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)) {
      if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
          file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) {
        continue;
      }

      var text = File.ReadAllText(file);
      if (!TestAttributeRegex.IsMatch(text)) {
        continue;
      }

      var ns = NamespaceRegex.Match(text);
      if (!ns.Success) {
        continue;
      }

      foreach (Match cls in ClassRegex.Matches(text)) {
        result.Add($"{ns.Groups[1].Value}.{cls.Groups[1].Value}");
      }
    }

    return result;
  }

  // Applies a filter string (terms joined by "|" or by "&") to a class name.
  private static bool Matches(string filter, string className) {
    if (string.IsNullOrEmpty(filter)) {
      return true;
    }

    bool Term(string term) {
      if (term.StartsWith("FullyQualifiedName!~", StringComparison.Ordinal)) {
        return !className.Contains(term["FullyQualifiedName!~".Length..], StringComparison.Ordinal);
      }

      return className.Contains(term["FullyQualifiedName~".Length..], StringComparison.Ordinal);
    }

    return filter.Contains('|', StringComparison.Ordinal)
      ? filter.Split('|').Any(Term)
      : filter.Split('&').All(Term);
  }

  // Each script call starts PowerShell, so the answers are kept for the run.
  private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> NameCache = new();
  private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> FilterCache = new();

  private static string[] ShardNames(string project) => NameCache.GetOrAdd(project, LoadShardNames);

  private static string[] LoadShardNames(string project) {
    var lines = CiScriptRunner.Run("get-test-shard-filter.ps1", "-Project", project, "-List");
    return JsonSerializer.Deserialize<string[]>(string.Concat(lines))!;
  }

  private static string Filter(string project, string shard) => FilterCache.GetOrAdd(project + "/" + shard, _ => LoadFilter(project, shard));

  private static string LoadFilter(string project, string shard) {
    var lines = CiScriptRunner.Run("get-test-shard-filter.ps1", "-Project", project, "-Shard", shard);
    return lines.Count == 0 ? string.Empty : lines[0];
  }

  [Theory]
  [InlineData("unit")]
  [InlineData("integration")]
  [InlineData("contract")]
  public void EveryTestClassIsInExactlyOneShard(string project) {
    var classes = FindTestClasses(project);
    classes.Should().NotBeEmpty();
    var filters = ShardNames(project).ToDictionary(name => name, name => Filter(project, name));

    foreach (var cls in classes) {
      var owners = filters.Where(f => Matches(f.Value, cls)).Select(f => f.Key).ToList();
      owners.Should().ContainSingle($"{cls} must be in exactly one shard of {project}");
    }
  }

  [Theory]
  [InlineData("unit")]
  [InlineData("integration")]
  [InlineData("contract")]
  public void ANewTestClassFallsIntoTheComplementShard(string project) {
    var names = ShardNames(project);
    var made = $"GameBot.NewArea.BrandNewTests{project}";
    var owners = names.Where(n => Matches(Filter(project, n), made)).ToList();
    owners.Should().Equal(names[^1]);
  }

  [Theory]
  [InlineData("unit")]
  [InlineData("integration")]
  public void PinnedClassesAreInThePinnedShard(string project) {
    var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(CiScriptRunner.FindRepoRoot(), "scripts", "ci", "test-shards.json")));
    var existing = FindTestClasses(project);
    var names = ShardNames(project);

    foreach (var pin in manifest.RootElement.GetProperty(project).GetProperty("pinned").EnumerateArray()) {
      var pinned = pin.GetProperty("class").GetString()!;
      var pinnedShard = pin.GetProperty("shard").GetString()!;
      existing.Should().Contain(pinned, "a pinned class must exist");
      Matches(Filter(project, pinnedShard), pinned).Should().BeTrue($"{pinned} must run in {pinnedShard}");
      foreach (var other in names.Where(n => n != pinnedShard)) {
        Matches(Filter(project, other), pinned).Should().BeFalse($"{pinned} must not run in {other}");
      }
    }  }
}
