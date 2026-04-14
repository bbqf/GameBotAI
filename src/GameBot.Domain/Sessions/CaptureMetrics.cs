using System;

namespace GameBot.Domain.Sessions;

/// <summary>
/// Lightweight value object exposing capture loop metrics to API consumers.
/// </summary>
public sealed record CaptureMetrics(
    double? CaptureRateFps,
    long FrameCount,
    DateTimeOffset? LastCaptureUtc);
