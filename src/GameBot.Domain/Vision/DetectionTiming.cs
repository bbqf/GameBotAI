using System;
using System.Diagnostics;

namespace GameBot.Domain.Vision
{
    internal static class DetectionTiming
    {
        public static TimeSpan Measure(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            sw.Stop();
            return sw.Elapsed;
        }
    }
}
