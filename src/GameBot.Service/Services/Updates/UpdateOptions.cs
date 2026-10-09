namespace GameBot.Service.Services.Updates;

/// <summary>
/// Settings for the update check (section <c>Update</c>). The defaults point at the public
/// release repository. Tests and forks can change both values (FR-012).
/// </summary>
internal sealed class UpdateOptions {
  public const string SectionName = "Update";
  public const string DefaultRepository = "bbqf/GameBotAI";

  /// <summary>The GitHub repository in the form owner/name.</summary>
  public string Repository { get; set; } = DefaultRepository;

  /// <summary>The base address of the GitHub REST API.</summary>
  public Uri ApiBaseUrl { get; set; } = new("https://api.github.com");
}
