using System.Collections.Generic;
using GameBot.Domain.Commands;

namespace GameBot.Service.Services;

/// <summary>
/// Makes the device input of a PrimitiveTap step (feature 111). The input is a swipe with the same
/// start point and end point. A single tap has a duration of
/// <see cref="PrimitiveTapConfig.DefaultTapDurationMs"/>. A press and hold has a duration equal to
/// the hold duration of the step.
/// </summary>
internal static class PrimitiveTapInput {
  /// <summary>Makes the swipe input at the point (<paramref name="x"/>, <paramref name="y"/>).</summary>
  /// <param name="x">X coordinate of the point in device pixels.</param>
  /// <param name="y">Y coordinate of the point in device pixels.</param>
  /// <param name="holdMs">Hold duration of the step. Null or 0 gives a single tap.</param>
  public static GameBot.Emulator.Session.InputAction Create(int x, int y, int? holdMs) {
    var args = new Dictionary<string, object> { ["x1"] = x, ["y1"] = y, ["x2"] = x, ["y2"] = y };
    var durationMs = EffectiveHoldMs(holdMs) ?? PrimitiveTapConfig.DefaultTapDurationMs;
    return new GameBot.Emulator.Session.InputAction("swipe", args, null, durationMs);
  }

  /// <summary>Gives the hold duration when the step presses and holds, else null.</summary>
  /// <param name="holdMs">Hold duration of the step.</param>
  public static int? EffectiveHoldMs(int? holdMs) => holdMs is > 0 ? holdMs : null;
}
