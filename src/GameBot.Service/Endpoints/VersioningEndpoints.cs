using GameBot.Domain.Versioning;
using GameBot.Service.Models;

namespace GameBot.Service.Endpoints;

// Handlers are named methods rather than inline lambdas: the Roslyn taint
// analyzers (CA3xxx) analyze lambdas as part of the containing method, and
// their cost grows super-linearly with method body size.
internal static class VersioningEndpoints {
  public static IEndpointRouteBuilder MapVersioningEndpoints(this IEndpointRouteBuilder app) {
    app.MapPost("/versioning/resolve", ResolveVersion)
      .Accepts<GameBot.Service.Models.VersionResolveRequestModel>("application/json")
      .WithTags("Versioning")
      .WithName("ResolveVersion");

    app.MapPost("/installer/compare", CompareInstallerVersion)
      .Accepts<GameBot.Service.Models.InstallCompareRequestModel>("application/json")
      .WithTags("Installer")
      .WithName("CompareInstallerVersion");

    app.MapPost("/installer/same-build/decision", ResolveSameBuildDecision)
      .Accepts<GameBot.Service.Models.SameBuildDecisionRequestModel>("application/json")
      .WithTags("Installer")
      .WithName("ResolveSameBuildDecision");

    return app;
  }

  private static IResult ResolveVersion(GameBot.Service.Models.VersionResolveRequestModel request, VersionSourceLoader loader) {
    if (!Enum.TryParse<BuildContext>(request.BuildContext, ignoreCase: true, out var buildContext)) {
      return Results.BadRequest(new { code = "invalid_build_context", message = "buildContext must be one of: ci, local." });
    }

    var versioningDirectory = TryFindVersioningDirectory();
    VersionOverride? fileOverride = null;
    CiBuildCounter? fileCounter = null;
    var notes = new List<string>();

    if (!string.IsNullOrWhiteSpace(versioningDirectory)) {
      try {
        fileOverride = loader.LoadOverrideFromDirectory(versioningDirectory);
        fileCounter = loader.LoadCiBuildCounterFromDirectory(versioningDirectory);
        notes.Add("source:versioning-files");
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) {
        return Results.BadRequest(new { code = "invalid_versioning_sources", message = ex.Message });
      }
    }
    else {
      notes.Add("source:request-only");
    }

    var requestOverride = request.Override;
    var effectiveOverride = new VersionOverride {
      Major = requestOverride?.Major ?? request.Major ?? fileOverride?.Major,
      Minor = requestOverride?.Minor ?? request.Minor ?? fileOverride?.Minor,
      Patch = requestOverride?.Patch ?? request.Patch ?? fileOverride?.Patch
    };

    if (effectiveOverride.Major is not null || effectiveOverride.Minor is not null || effectiveOverride.Patch is not null) {
      notes.Add("source:override");
    }

    var requestCounter = request.CiBuildCounter?.LastBuild ?? request.LastCiBuild;
    var effectiveLastBuild = requestCounter ?? fileCounter?.LastBuild ?? 0;
    if (requestCounter.HasValue) {
      notes.Add("source:counter-request");
    }
    else if (fileCounter is not null) {
      notes.Add("source:counter-file");
    }

    var resolutionInput = new VersionResolutionInput {
      BaselineVersion = new SemanticVersion(1, 0, 0, Math.Max(0, effectiveLastBuild)),
      Override = effectiveOverride,
      CiBuildCounter = new CiBuildCounter {
        LastBuild = Math.Max(0, effectiveLastBuild),
        UpdatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedBy = string.Equals(buildContext, BuildContext.Ci) ? "ci" : "local"
      },
      Context = buildContext
    };

    var resolved = VersionResolutionService.Resolve(resolutionInput);
    var responseNotes = resolved.Notes.Concat(notes).Distinct(StringComparer.Ordinal).ToArray();

    return Results.Ok(new GameBot.Service.Models.VersionResolveResultModel {
      Version = new GameBot.Service.Models.SemanticVersionModel {
        Major = resolved.Version.Major,
        Minor = resolved.Version.Minor,
        Patch = resolved.Version.Patch,
        Build = resolved.Version.Build
      },
      Source = buildContext == BuildContext.Ci ? "ci" : "local",
      Persisted = resolved.ShouldPersistBuildCounter,
      Authoritative = resolved.IsAuthoritativeBuild,
      Notes = responseNotes
    });
  }

  private static IResult CompareInstallerVersion(GameBot.Service.Models.InstallCompareRequestModel request, SemanticVersionComparer comparer) {
    var installed = new SemanticVersion(
      request.InstalledVersion.Major,
      request.InstalledVersion.Minor,
      request.InstalledVersion.Patch,
      request.InstalledVersion.Build);
    var candidate = new SemanticVersion(
      request.CandidateVersion.Major,
      request.CandidateVersion.Minor,
      request.CandidateVersion.Patch,
      request.CandidateVersion.Build);

    var compare = comparer.Compare(candidate, installed);
    if (compare < 0) {
      return Results.Ok(new GameBot.Service.Models.InstallCompareResultModel {
        Outcome = "downgrade",
        Reason = "Candidate version is lower than installed version.",
        PreserveProperties = false
      });
    }

    if (compare > 0) {
      return Results.Ok(new GameBot.Service.Models.InstallCompareResultModel {
        Outcome = "upgrade",
        Reason = "Candidate version is higher than installed version.",
        PreserveProperties = true
      });
    }

    return Results.Ok(new GameBot.Service.Models.InstallCompareResultModel {
      Outcome = "sameBuild",
      Reason = "Candidate version equals installed version.",
      PreserveProperties = false
    });
  }

  private static IResult ResolveSameBuildDecision(GameBot.Service.Models.SameBuildDecisionRequestModel request) {
    if (string.Equals(request.Mode, "unattended", StringComparison.OrdinalIgnoreCase)) {
      return Results.Ok(new GameBot.Service.Models.SameBuildDecisionResultModel {
        Action = "skip",
        MutatesState = false,
        StatusCode = 4090
      });
    }

    if (string.Equals(request.InteractiveChoice, "reinstall", StringComparison.OrdinalIgnoreCase)) {
      return Results.Ok(new GameBot.Service.Models.SameBuildDecisionResultModel {
        Action = "reinstall",
        MutatesState = true,
        StatusCode = 0
      });
    }

    return Results.Ok(new GameBot.Service.Models.SameBuildDecisionResultModel {
      Action = "cancel",
      MutatesState = false,
      StatusCode = 0
    });
  }

  private static string? TryFindVersioningDirectory() {
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null) {
      var candidate = Path.Combine(directory.FullName, "installer", "versioning");
      if (Directory.Exists(candidate)) {
        return candidate;
      }

      directory = directory.Parent;
    }

    return null;
  }
}
