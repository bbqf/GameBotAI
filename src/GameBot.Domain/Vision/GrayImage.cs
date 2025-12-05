using System.Diagnostics.CodeAnalysis;

namespace GameBot.Domain.Vision;

[SuppressMessage("Performance", "CA1819:Properties should not return arrays")]
public sealed class GrayImage {
  public int Width { get; }
  public int Height { get; }
  public byte[] Data { get; }
  public GrayImage(int w, int h, byte[] data) { Width = w; Height = h; Data = data; }
}
