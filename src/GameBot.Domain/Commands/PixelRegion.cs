using System;
using System.Collections.Generic;

namespace GameBot.Domain.Commands;

/// <summary>
/// A rectangle in capture pixels (feature 130). It covers the pixels X to X + Width - 1 and
/// Y to Y + Height - 1. This type holds the one validation rule and the one clip rule for the
/// region field. No other code keeps its own copy of these rules.
/// </summary>
public sealed class PixelRegion {
  public int X { get; }
  public int Y { get; }
  public int Width { get; }
  public int Height { get; }

  public PixelRegion(int x, int y, int width, int height) {
    X = x;
    Y = y;
    Width = width;
    Height = height;
  }

  /// <summary>
  /// Checks the four fields of a region. Returns one message for each invalid field. The list is empty
  /// when the region is valid. A missing field gives "is required".
  /// </summary>
  /// <param name="x">Left edge. 0 or more.</param>
  /// <param name="y">Top edge. 0 or more.</param>
  /// <param name="width">More than 0.</param>
  /// <param name="height">More than 0.</param>
  /// <param name="prefix">Path text put before each field name, for example <c>region</c>.</param>
  public static IReadOnlyList<string> Validate(int? x, int? y, int? width, int? height, string prefix = "region") {
    var errors = new List<string>();
    CheckOrigin(errors, prefix, "x", x);
    CheckOrigin(errors, prefix, "y", y);
    CheckSize(errors, prefix, "width", width);
    CheckSize(errors, prefix, "height", height);
    return errors;
  }

  /// <summary>Checks this region with the same rule as the static method.</summary>
  public IReadOnlyList<string> Validate(string prefix = "region") => Validate(X, Y, Width, Height, prefix);

  /// <summary>
  /// Returns the part of the region that is inside a capture, or null when no pixel remains.
  /// </summary>
  public PixelRegion? ClipTo(int captureWidth, int captureHeight) {
    if (captureWidth <= 0 || captureHeight <= 0 || Width <= 0 || Height <= 0) return null;
    var left = Math.Max(0, (long)X);
    var top = Math.Max(0, (long)Y);
    var right = Math.Min(captureWidth, (long)X + Width);
    var bottom = Math.Min(captureHeight, (long)Y + Height);
    if (right <= left || bottom <= top) return null;
    return new PixelRegion((int)left, (int)top, (int)(right - left), (int)(bottom - top));
  }

  private static void CheckOrigin(List<string> errors, string prefix, string name, int? value) {
    if (value is null) errors.Add($"{prefix}.{name} is required");
    else if (value < 0) errors.Add($"{prefix}.{name} must be 0 or more");
  }

  private static void CheckSize(List<string> errors, string prefix, string name, int? value) {
    if (value is null) errors.Add($"{prefix}.{name} is required");
    else if (value <= 0) errors.Add($"{prefix}.{name} must be greater than 0");
  }

  /// <summary>Text for logs: <c>x,y,width,height</c>.</summary>
  public string Describe() => $"{X},{Y},{Width},{Height}";
}
