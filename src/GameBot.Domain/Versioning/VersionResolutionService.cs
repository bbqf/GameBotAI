using System;
using System.Collections.Generic;

namespace GameBot.Domain.Versioning;

public enum BuildContext
{
    Ci = 0,
    Local = 1
}

public sealed record class VersionResolutionInput
{
    public required SemanticVersion BaselineVersion { get; init; }
    public VersionOverride? Override { get; init; }
    public bool ReleaseLineTransitionDetected { get; init; }
    public int? PreviousReleaseLineSequence { get; init; }
    public int? CurrentReleaseLineSequence { get; init; }
    public required CiBuildCounter CiBuildCounter { get; init; }
    public required BuildContext Context { get; init; }
}

public sealed record class VersionResolutionResult
{
    public required SemanticVersion Version { get; init; }
    public bool ShouldPersistBuildCounter { get; init; }
    public bool IsAuthoritativeBuild { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Applies semver policy and source precedence to produce an effective installer version.
/// </summary>
public sealed class VersionResolutionService
{
    public static bool HasReleaseLineTransition(int? previousSequence, int? currentSequence)
    {
        if (!previousSequence.HasValue || !currentSequence.HasValue)
        {
            return false;
        }

        return currentSequence.Value > previousSequence.Value;
    }

    public static VersionResolutionResult Resolve(VersionResolutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var notes = new List<string>();
        var baseline = input.BaselineVersion;
        var versionOverride = input.Override;
        var releaseLineTransitionDetected = input.ReleaseLineTransitionDetected ||
            HasReleaseLineTransition(input.PreviousReleaseLineSequence, input.CurrentReleaseLineSequence);

        var major = versionOverride?.Major ?? baseline.Major;

        int minor;
        if (versionOverride?.Minor is int manualMinor)
        {
            minor = manualMinor;
            notes.Add("minor:override");
        }
        else if (releaseLineTransitionDetected)
        {
            minor = baseline.Minor + 1;
            notes.Add("minor:auto-transition");
        }
        else
        {
            minor = baseline.Minor;
            notes.Add("minor:baseline");
        }

        int patch;
        if (versionOverride?.Patch is int manualPatch)
        {
            patch = manualPatch;
            notes.Add("patch:override");
        }
        else if (releaseLineTransitionDetected && versionOverride?.Minor is null)
        {
            patch = 0;
            notes.Add("patch:auto-reset");
        }
        else
        {
            patch = baseline.Patch;
            notes.Add("patch:baseline");
        }

        var nextBuild = checked(input.CiBuildCounter.LastBuild + 1);
        var shouldPersist = input.Context == BuildContext.Ci;

        if (shouldPersist)
        {
            notes.Add("build:ci-authoritative");
        }
        else
        {
            notes.Add("build:local-derived-no-persist");
        }

        var resolved = new SemanticVersion(major, minor, patch, nextBuild);
        return new VersionResolutionResult
        {
            Version = resolved,
            ShouldPersistBuildCounter = shouldPersist,
            IsAuthoritativeBuild = shouldPersist,
            Notes = notes
        };
    }
}
