using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBot.Domain.Versioning;

public sealed record class VersionOverride
{
    [JsonPropertyName("major")]
    public int? Major { get; init; }

    [JsonPropertyName("minor")]
    public int? Minor { get; init; }

    [JsonPropertyName("patch")]
    public int? Patch { get; init; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset? UpdatedAtUtc { get; init; }
}

public sealed record class ReleaseLineMarker
{
    [JsonPropertyName("releaseLineId")]
    public string ReleaseLineId { get; init; } = string.Empty;

    [JsonPropertyName("sequence")]
    public int Sequence { get; init; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; init; }
}

public sealed record class CiBuildCounter
{
    [JsonPropertyName("lastBuild")]
    public int LastBuild { get; init; }

    [JsonPropertyName("updatedAtUtc")]
    public DateTimeOffset UpdatedAtUtc { get; init; }

    [JsonPropertyName("updatedBy")]
    public string UpdatedBy { get; init; } = string.Empty;
}

/// <summary>
/// Loads checked-in versioning source files used by version resolution.
/// </summary>
public sealed class VersionSourceLoader
{
    public const string OverrideFileName = "version.override.json";
    public const string ReleaseLineMarkerFileName = "release-line.marker.json";
    public const string CiBuildCounterFileName = "ci-build-counter.json";

    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public VersionOverride LoadOverride(string filePath) => LoadRequired<VersionOverride>(filePath);

    public ReleaseLineMarker LoadReleaseLineMarker(string filePath) => LoadRequired<ReleaseLineMarker>(filePath);

    public CiBuildCounter LoadCiBuildCounter(string filePath) => LoadRequired<CiBuildCounter>(filePath);

    public VersionOverride LoadOverrideFromDirectory(string directoryPath) =>
        LoadOverride(Path.Combine(directoryPath, OverrideFileName));

    public ReleaseLineMarker LoadReleaseLineMarkerFromDirectory(string directoryPath) =>
        LoadReleaseLineMarker(Path.Combine(directoryPath, ReleaseLineMarkerFileName));

    public CiBuildCounter LoadCiBuildCounterFromDirectory(string directoryPath) =>
        LoadCiBuildCounter(Path.Combine(directoryPath, CiBuildCounterFileName));

    private T LoadRequired<T>(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path must be provided.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Required versioning file not found: {filePath}", filePath);
        }

        var json = File.ReadAllText(filePath);
        var model = JsonSerializer.Deserialize<T>(json, _serializerOptions);
        if (model is null)
        {
            throw new InvalidDataException($"Versioning file '{filePath}' could not be parsed.");
        }

        return model;
    }
}
