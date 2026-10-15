using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace AirportPlanning.Components
{
    /// <summary>
    /// Shared conversions between Grasshopper's Rhino types and the Rhino-free
    /// core domain types, so the facility components stay free of duplicated
    /// parsing rules.
    /// </summary>
    internal static class FacilityComponentInput
    {
        /// <summary>
        /// Converts an optional Domain input into optimizer bounds. An unwired or
        /// degenerate interval falls back to the legacy 1..1200 scan window.
        /// </summary>
        public static ResourceSearchRange ToResourceDomain(Interval domain)
        {
            double lower = domain.Min;
            double upper = domain.Max;

            bool usable =
                !double.IsNaN(lower) && !double.IsNaN(upper) &&
                !double.IsInfinity(lower) && !double.IsInfinity(upper) &&
                upper >= lower && upper >= 1.0;

            if (!usable)
            {
                return new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources);
            }

            int min = (int)Math.Round(lower);
            int max = (int)Math.Round(upper);

            return new ResourceSearchRange(min, max).Normalized();
        }

        /// <summary>
        /// Names the pools in output order: one entry per dedicated channel, or a
        /// single "Shared" entry when the facility runs one shared pool.
        /// Channel names are normalized exactly like the optimizer's pool
        /// resolver (trimmed, blanks dropped, duplicates removed), so the names
        /// always line up with the per-pool outputs.
        /// </summary>
        public static List<string> GetPoolNames(List<string> channels)
        {
            List<string> names = NormalizeChannels(channels);

            if (names.Count == 0)
                names.Add("Shared");

            return names;
        }

        /// <summary>
        /// Wait95 per pool, aligned with <see cref="GetPoolNames"/>. A shared pool
        /// reports the facility-level whole-run value (the same metric the
        /// optimizer sizes against).
        /// </summary>
        public static List<double> GetWaitPerPool(
            Dictionary<string, double> waitPerChannel,
            QueueResults results,
            List<string> channels)
        {
            List<double> waits = new List<double>();

            List<string> normalized = NormalizeChannels(channels);

            if (normalized.Count == 0)
            {
                waits.Add(results != null ? results.Wait95 : 0.0);
                return waits;
            }

            foreach (string key in normalized)
            {
                if (waitPerChannel != null && waitPerChannel.TryGetValue(key, out double value))
                    waits.Add(value);
                else
                    waits.Add(0.0);
            }

            return waits;
        }

        /// <summary>
        /// Mirrors the optimizer pool resolver's channel normalization: trim,
        /// drop blanks, drop case-insensitive duplicates, keep first-seen order.
        /// </summary>
        private static List<string> NormalizeChannels(IList<string> channels)
        {
            List<string> result = new List<string>();

            if (channels == null)
                return result;

            HashSet<string> seen =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string channel in channels)
            {
                if (string.IsNullOrWhiteSpace(channel))
                    continue;

                string trimmed = channel.Trim();

                if (seen.Add(trimmed))
                    result.Add(trimmed);
            }

            return result;
        }
    }
}
