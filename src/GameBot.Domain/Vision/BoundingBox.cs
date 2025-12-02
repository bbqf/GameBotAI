using System;

namespace GameBot.Domain.Vision
{
    public readonly record struct BoundingBox(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public int Area => Math.Max(0, Width) * Math.Max(0, Height);

        public static double IoU(BoundingBox a, BoundingBox b)
        {
            var x1 = Math.Max(a.X, b.X);
            var y1 = Math.Max(a.Y, b.Y);
            var x2 = Math.Min(a.Right, b.Right);
            var y2 = Math.Min(a.Bottom, b.Bottom);

            var iw = Math.Max(0, x2 - x1);
            var ih = Math.Max(0, y2 - y1);
            var inter = iw * ih;
            if (inter == 0) return 0;
            var union = a.Area + b.Area - inter;
            if (union <= 0) return 0;
            return (double)inter / union;
        }
    }
}
