using System;
using System.Collections.Generic;

namespace AirportPlanning.Core.Queueing
{
    /// <summary>
    /// Builds the per-pool sizing plans from the raw lists supplied by a
    /// Grasshopper facility component.
    ///
    /// List handling rules:
    ///   * an empty channel list produces a single shared pool (legacy behaviour);
    ///   * a single-item list broadcasts to every channel;
    ///   * a longer list must match the channel count, otherwise the first value
    ///     is used and a warning is raised.
    ///
    /// The search bounds come from one broadcast domain, so every channel shares
    /// the same [Min, Max] window unless the inputs are widened later.
    /// </summary>
    public static class ChannelResourcePlanResolver
    {
        /// <summary>Upper bound used when no domain is wired, matching the legacy scan cap.</summary>
        public const int DefaultMaxResources = 1200;

        /// <summary>Target wait used when none is supplied.</summary>
        public const double DefaultTargetWait95 = 10.0;

        /// <summary>Physical count used when none is supplied and optimization is off.</summary>
        public const int DefaultPhysicalResources = 1;

        public static List<ChannelResourcePlan> Resolve(
            IList<string> channels,
            IList<int> physicalResources,
            IList<double> targetWaits,
            ResourceSearchRange range,
            Action<string> warn)
        {
            ResourceSearchRange bounds = (range ?? new ResourceSearchRange(1, DefaultMaxResources)).Normalized();

            List<ChannelResourcePlan> plans = new List<ChannelResourcePlan>();

            List<string> normalizedChannels = NormalizeChannels(channels, warn);

            if (normalizedChannels.Count == 0)
            {
                plans.Add(new ChannelResourcePlan
                {
                    Channel = null,
                    PhysicalResources = PickPhysical(physicalResources),
                    TargetWait95 = PickTarget(targetWaits),
                    Range = bounds
                });

                return plans;
            }

            WarnOnLengthMismatch(physicalResources, normalizedChannels.Count, "Physical Count", warn);
            WarnOnLengthMismatch(targetWaits, normalizedChannels.Count, "Target Wait", warn);

            for (int i = 0; i < normalizedChannels.Count; i++)
            {
                plans.Add(new ChannelResourcePlan
                {
                    Channel = normalizedChannels[i],
                    PhysicalResources = PickPhysical(physicalResources, i),
                    TargetWait95 = PickTarget(targetWaits, i),
                    Range = bounds
                });
            }

            return plans;
        }

        private static List<string> NormalizeChannels(IList<string> channels, Action<string> warn)
        {
            List<string> result = new List<string>();

            if (channels == null)
                return result;

            HashSet<string> seen =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < channels.Count; i++)
            {
                string channel = channels[i];

                if (string.IsNullOrWhiteSpace(channel))
                    continue;

                string trimmed = channel.Trim();

                if (!seen.Add(trimmed))
                {
                    warn?.Invoke($"Channel '{trimmed}' is listed more than once; duplicates are ignored.");
                    continue;
                }

                result.Add(trimmed);
            }

            return result;
        }

        private static void WarnOnLengthMismatch(
            IList<int> values,
            int channelCount,
            string label,
            Action<string> warn)
        {
            if (values == null || values.Count == 0)
                return;

            if (values.Count == 1)
                return;

            if (values.Count == channelCount)
                return;

            warn?.Invoke(
                $"{label} has {values.Count} values but {channelCount} channels are listed; " +
                "the first value is used for every channel.");
        }

        private static void WarnOnLengthMismatch(
            IList<double> values,
            int channelCount,
            string label,
            Action<string> warn)
        {
            if (values == null || values.Count == 0)
                return;

            if (values.Count == 1)
                return;

            if (values.Count == channelCount)
                return;

            warn?.Invoke(
                $"{label} has {values.Count} values but {channelCount} channels are listed; " +
                "the first value is used for every channel.");
        }

        private static int PickPhysical(IList<int> values)
        {
            if (values == null || values.Count == 0)
                return DefaultPhysicalResources;

            int value = values[0];

            return value < 1 ? 1 : value;
        }

        private static int PickPhysical(IList<int> values, int index)
        {
            if (values == null || values.Count == 0)
                return DefaultPhysicalResources;

            int slot = values.Count == 1 ? 0 : Math.Min(index, values.Count - 1);

            int value = values[slot];

            return value < 1 ? 1 : value;
        }

        private static double PickTarget(IList<double> values)
        {
            if (values == null || values.Count == 0)
                return DefaultTargetWait95;

            return values[0] <= 0.0 ? 0.0 : values[0];
        }

        private static double PickTarget(IList<double> values, int index)
        {
            if (values == null || values.Count == 0)
                return DefaultTargetWait95;

            int slot = values.Count == 1 ? 0 : Math.Min(index, values.Count - 1);

            return values[slot] <= 0.0 ? 0.0 : values[slot];
        }
    }
}
