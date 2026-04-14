using System;
using System.Drawing;
using System.Runtime.Versioning;

namespace GameBot.Domain.Sessions;

/// <summary>
/// Immutable snapshot of a single captured screenshot, held in memory per session.
/// Both PNG bytes and decoded Bitmap are cached so consumers choose their preferred format.
/// </summary>
[SupportedOSPlatform("windows")]
#pragma warning disable CA1819 // PngBytes is an immutable PNG snapshot, not a mutable array property
public sealed record CachedFrame(
    byte[] PngBytes,
    Bitmap Bitmap,
    DateTimeOffset Timestamp,
    int Width,
    int Height);
#pragma warning restore CA1819
