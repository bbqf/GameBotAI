namespace GameBot.Service.Services.Updates;

/// <summary>The file layout of the update feature under the data root (data-model.md, persistence layout).</summary>
internal sealed class UpdatePaths {
  public const string ResultFileName = "update-result.json";
  public const string ReportedResultFileName = "update-result.reported.json";

  public UpdatePaths(string dataRoot) {
    DataRoot = dataRoot;
    Directory = Path.Combine(dataRoot, "updates");
  }

  public string DataRoot { get; }

  /// <summary>The folder <c>&lt;data root&gt;\updates</c>.</summary>
  public string Directory { get; }

  /// <summary>The folder for the copy of the updater files.</summary>
  public string UpdaterCopyDirectory => Path.Combine(Directory, "updater");

  public string ResultFile => Path.Combine(Directory, ResultFileName);

  public string ReportedResultFile => Path.Combine(Directory, ReportedResultFileName);

  public string MsiPath(string version) => Path.Combine(Directory, $"GameBot-{version}.msi");

  public string MsiexecLogPath(Guid attemptId) => Path.Combine(Directory, $"msiexec-{attemptId:N}.log");
}
