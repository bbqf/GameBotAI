using System.Text.Json;

namespace GameBot.Service.StartupValidation;

internal interface ILegacyActionReferenceScanner {
  Task<CutoverValidationReport> ScanAsync(CancellationToken cancellationToken = default);
}

internal sealed class LegacyActionReferenceScanner : ILegacyActionReferenceScanner {
  private readonly string _storageRoot;
  private readonly ILogger<LegacyActionReferenceScanner> _logger;
  private readonly Func<DateTime> _utcNow;

  public LegacyActionReferenceScanner(string storageRoot, ILogger<LegacyActionReferenceScanner> logger, Func<DateTime>? utcNow = null) {
    ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
    ArgumentNullException.ThrowIfNull(logger);
    _storageRoot = storageRoot;
    _logger = logger;
    _utcNow = utcNow ?? (() => DateTime.UtcNow);
  }

  public async Task<CutoverValidationReport> ScanAsync(CancellationToken cancellationToken = default) {
    var issues = new List<CutoverValidationIssue>();

    ScanLegacyActionStore(issues);
    await ScanCommandStoreAsync(issues, cancellationToken).ConfigureAwait(false);

    return issues.Count == 0
      ? CutoverValidationReport.Clean(_utcNow())
      : CutoverValidationReport.Blocked(_utcNow(), issues);
  }

  private void ScanLegacyActionStore(List<CutoverValidationIssue> issues) {
    var actionsRoot = Path.Combine(_storageRoot, "actions");
    if (!Directory.Exists(actionsRoot)) {
      return;
    }

    foreach (var filePath in Directory.EnumerateFiles(actionsRoot, "*.json", SearchOption.AllDirectories)) {
      var relativePath = Path.GetRelativePath(_storageRoot, filePath);
      issues.Add(new CutoverValidationIssue {
        Store = "actions",
        Path = relativePath,
        Message = "Legacy Action records still exist.",
        ReferenceCount = 1
      });
    }
  }

  private async Task ScanCommandStoreAsync(List<CutoverValidationIssue> issues, CancellationToken cancellationToken) {
    var commandsRoot = Path.Combine(_storageRoot, "commands");
    if (!Directory.Exists(commandsRoot)) {
      return;
    }

    foreach (var filePath in Directory.EnumerateFiles(commandsRoot, "*.json", SearchOption.AllDirectories)) {
      cancellationToken.ThrowIfCancellationRequested();

      var legacyReferenceCount = await CountLegacyReferencesAsync(filePath, cancellationToken).ConfigureAwait(false);
      if (legacyReferenceCount == 0) {
        continue;
      }

      var relativePath = Path.GetRelativePath(_storageRoot, filePath);
      issues.Add(new CutoverValidationIssue {
        Store = "commands",
        Path = relativePath,
        Message = "Legacy Action step references still exist.",
        ReferenceCount = legacyReferenceCount
      });
    }
  }

  private static async Task<int> CountLegacyReferencesAsync(string filePath, CancellationToken cancellationToken) {
    using var stream = File.OpenRead(filePath);
    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    return CountLegacyReferences(document.RootElement);
  }

  private static int CountLegacyReferences(JsonElement element) {
    var count = 0;

    switch (element.ValueKind) {
      case JsonValueKind.Object:
        foreach (var property in element.EnumerateObject()) {
          if (IsLegacyActionTypeProperty(property)) {
            count++;
          }

          count += CountLegacyReferences(property.Value);
        }
        break;
      case JsonValueKind.Array:
        foreach (var item in element.EnumerateArray()) {
          count += CountLegacyReferences(item);
        }
        break;
    }

    return count;
  }

  private static bool IsLegacyActionTypeProperty(JsonProperty property) {
    if (!property.NameEquals("type")) {
      return false;
    }

    return property.Value.ValueKind switch {
      JsonValueKind.String => string.Equals(property.Value.GetString(), "Action", StringComparison.OrdinalIgnoreCase),
      JsonValueKind.Number => property.Value.TryGetInt32(out var value) && value == 1,
      _ => false
    };
  }
}