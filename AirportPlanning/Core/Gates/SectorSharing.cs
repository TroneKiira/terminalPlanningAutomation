using System;

namespace AirportPlanning.Core.Gates
{
    /// <summary>
    /// One aircraft code's sector sharing, derived from measured schedule peaks.
    ///
    /// Three curves feed it: the domestic sector peak (D), the international
    /// sector peak (I), the busiest simultaneous total (C, the combined peak),
    /// and the busiest simultaneous count of mixed-sector rotations (M), whose
    /// aircraft must hold one swing stand for the whole turn.
    ///
    /// The stand requirement is the CONCURRENT peak, never the sum of the
    /// sector peaks: stands serve both sectors at different times of day, so
    /// the total is C and the difference D + I - C is the swing group. The
    /// "concurrent floor" is the invariant that the sized total is never below
    /// C. Swing is floored by the mixed peak, because a mixed rotation cannot
    /// stand dedicated to either sector: when M exceeds the implied swing, the
    /// dedicated groups shrink and swing rises to cover M while the total
    /// still equals C.
    ///
    /// The sum of the sector peaks (D + I) is reported alongside as
    /// <see cref="Result.SectorSum"/> so a planner can read what a
    /// no-sharing, sector-dedicated terminal would demand.
    /// </summary>
    public static class SectorSharing
    {
        public static Result Calculate(
            int domesticPeak,
            int internationalPeak,
            int combinedPeak,
            int mixedPeak)
        {
            int d = Math.Max(0, domesticPeak);
            int i = Math.Max(0, internationalPeak);
            int c = Math.Max(0, combinedPeak);
            int m = Math.Max(0, mixedPeak);

            Result result = new Result
            {
                DomesticPeak = d,
                InternationalPeak = i,
                CombinedPeak = c,
                MixedPeak = m,
                SectorSum = d + i
            };

            int impliedSwing = Math.Max(0, d + i - c);
            result.MixedFloor = impliedSwing < m;

            if (!result.MixedFloor)
            {
                result.Swing = impliedSwing;
                result.DedicatedDomestic = Math.Max(0, c - i);
                result.DedicatedInternational = Math.Max(0, c - d);
            }
            else
            {
                result.DedicatedDomestic = Math.Max(0, d - m);
                result.DedicatedInternational = Math.Max(0, i - m);
                result.Swing = Math.Max(0, c - result.DedicatedDomestic - result.DedicatedInternational);
            }

            result.Total = result.DedicatedDomestic + result.DedicatedInternational + result.Swing;
            result.ConcurrentFloorMet = result.Total >= c;

            return result;
        }

        public class Result
        {
            /// <summary>Domestic sector peak (mixed rotations included).</summary>
            public int DomesticPeak { get; set; }

            /// <summary>International sector peak (mixed rotations included).</summary>
            public int InternationalPeak { get; set; }

            /// <summary>Busiest simultaneous stand count: the sizing basis.</summary>
            public int CombinedPeak { get; set; }

            /// <summary>Busiest simultaneous count of mixed-sector rotations.</summary>
            public int MixedPeak { get; set; }

            /// <summary>
            /// Sum of the sector peaks, D + I. What a sector-dedicated terminal
            /// would build; reported, never used to size.
            /// </summary>
            public int SectorSum { get; set; }

            /// <summary>Stands able to serve either sector.</summary>
            public int Swing { get; set; }

            /// <summary>Stands serving the domestic sector only.</summary>
            public int DedicatedDomestic { get; set; }

            /// <summary>Stands serving the international sector only.</summary>
            public int DedicatedInternational { get; set; }

            /// <summary>
            /// Dedicated domestic + dedicated international + swing: the sized
            /// total, floored by the concurrent peak.
            /// </summary>
            public int Total { get; set; }

            /// <summary>True when the mixed peak forced swing above its implied value.</summary>
            public bool MixedFloor { get; set; }

            /// <summary>True when the sized total meets the concurrent peak.</summary>
            public bool ConcurrentFloorMet { get; set; }
        }
    }
}
