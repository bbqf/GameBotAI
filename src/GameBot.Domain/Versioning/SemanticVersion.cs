using System;

namespace GameBot.Domain.Versioning;

/// <summary>
/// Represents a semantic version composed of four non-negative numeric components.
/// </summary>
public readonly record struct SemanticVersion
{
    public SemanticVersion(int major, int minor, int patch, int build)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        ArgumentOutOfRangeException.ThrowIfNegative(build);

        Major = major;
        Minor = minor;
        Patch = patch;
        Build = build;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public int Build { get; }

    public override string ToString() => $"{Major}.{Minor}.{Patch}.{Build}";

    public static SemanticVersion Parse(string value)
    {
        if (!TryParse(value, out var version))
        {
            throw new FormatException($"Invalid semantic version '{value}'. Expected format: <Major>.<Minor>.<Patch>.<Build>.");
        }

        return version;
    }

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var major) || major < 0)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var minor) || minor < 0)
        {
            return false;
        }

        if (!int.TryParse(parts[2], out var patch) || patch < 0)
        {
            return false;
        }

        if (!int.TryParse(parts[3], out var build) || build < 0)
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, build);
        return true;
    }
}
