using System.Collections.Generic;

namespace GameBot.Domain.Versioning;

/// <summary>
/// Compares semantic versions lexicographically using Major, Minor, Patch, then Build.
/// </summary>
public sealed class SemanticVersionComparer : IComparer<SemanticVersion>
{
    public int Compare(SemanticVersion x, SemanticVersion y)
    {
        var major = x.Major.CompareTo(y.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = x.Minor.CompareTo(y.Minor);
        if (minor != 0)
        {
            return minor;
        }

        var patch = x.Patch.CompareTo(y.Patch);
        if (patch != 0)
        {
            return patch;
        }

        return x.Build.CompareTo(y.Build);
    }
}
