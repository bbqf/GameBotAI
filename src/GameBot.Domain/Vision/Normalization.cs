using System;

namespace GameBot.Domain.Vision
{
    public static class Normalization
    {
        public static void NormalizeRect(int x, int y, int width, int height, int imageWidth, int imageHeight,
            out double nx, out double ny, out double nw, out double nh)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageWidth);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(imageHeight);
            nx = Clamp01((double)x / imageWidth);
            ny = Clamp01((double)y / imageHeight);
            nw = Clamp01((double)width / imageWidth);
            nh = Clamp01((double)height / imageHeight);
        }

        public static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        public static double ClampConfidence(double v) => Clamp01(v);
    }
}
