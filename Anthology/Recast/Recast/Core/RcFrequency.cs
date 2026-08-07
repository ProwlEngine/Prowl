using System;
using System.Diagnostics;

namespace Prowl.Recast.Core
{
    public static class RcFrequency
    {
        public static readonly double Frequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
        public static long Ticks => unchecked((long)(Stopwatch.GetTimestamp() * Frequency));
    }
}