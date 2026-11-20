namespace GameBot.Domain.Sessions;

/// <summary>
/// The data of one read of the frame in memory by a step (feature 197).
/// </summary>
/// <param name="FrameId">The id of the frame (<see cref="CachedFrame.FrameId"/>).</param>
/// <param name="AgeMs">The age of the frame at the read, in milliseconds.</param>
/// <param name="Stale">True when the frame was stale at the read. A step does not use a stale frame.</param>
public sealed record FrameReadInfo(string FrameId, long AgeMs, bool Stale);
