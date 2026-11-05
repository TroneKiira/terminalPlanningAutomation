using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Inputs;

namespace AirportPlanning.Core.Queueing
{
    /// <summary>
    /// Builds per-pool plans from channel lists and optional channel subdivisions.
    /// Empty channels retain the legacy shared-pool behavior. Pool-level settings
    /// align with the expanded plan order (a single setting broadcasts).
    /// </summary>
    public static class ChannelResourcePlanResolver
    {
        public const int DefaultMaxResources = 1200;
        public const double DefaultTargetWait95 = 10.0;
        public const int DefaultPhysicalResources = 1;

        public static List<ChannelResourcePlan> Resolve(
            IList<string> channels,
            IList<int> physicalResources,
            IList<double> targetWaits,
            ResourceSearchRange range,
            Action<string> warn)
        {
            return Resolve(channels, physicalResources, targetWaits, null, null, null,
                null, null, range, warn);
        }

        public static List<ChannelResourcePlan> Resolve(
            IList<string> channels,
            IList<int> physicalResources,
            IList<double> targetWaits,
            IList<IList<string>> poolKeysPerChannel,
            ResourceSearchRange range,
            Action<string> warn)
        {
            return Resolve(channels, physicalResources, targetWaits, poolKeysPerChannel,
                null, null, null, null, range, warn);
        }

        /// <summary>
        /// Resolves channel plans and expands any channel with pool keys. Pool
        /// settings align with the resulting expanded plan list, e.g. Counter's
        /// four sector/class queues followed by Kiosk and BagDrop.
        /// </summary>
        public static List<ChannelResourcePlan> Resolve(
            IList<string> channels,
            IList<int> physicalResources,
            IList<double> targetWaits,
            IList<IList<string>> poolKeysPerChannel,
            IList<int> poolPhysicalResources,
            IList<double> poolTargetWaits,
            IList<int> poolMinimumResources,
            IList<int> poolMaximumResources,
            ResourceSearchRange range,
            Action<string> warn)
        {
            ResourceSearchRange defaultBounds =
                (range ?? new ResourceSearchRange(1, DefaultMaxResources)).Normalized();

            List<string> normalizedChannels = NormalizeChannels(channels, warn);
            List<ChannelResourcePlan> plans = new List<ChannelResourcePlan>();

            if (normalizedChannels.Count == 0)
            {
                if (poolKeysPerChannel != null &&
                    poolKeysPerChannel.Any(keys => keys != null && keys.Count > 0))
                {
                    warn?.Invoke("Pool Keys were supplied without Channels and are ignored; the legacy shared pool is used.");
                }

                plans.Add(new ChannelResourcePlan
                {
                    Channel = null,
                    PhysicalResources = PickPhysical(physicalResources),
                    TargetWait95 = PickTarget(targetWaits),
                    Range = ResolveRange(defaultBounds, poolMinimumResources, poolMaximumResources, 0, 1, warn)
                });

                ApplyPoolOverrides(plans, poolPhysicalResources, poolTargetWaits,
                    poolMinimumResources, poolMaximumResources, defaultBounds, warn);
                return plans;
            }

            WarnOnLengthMismatch(physicalResources, normalizedChannels.Count, "Physical Count", warn);
            WarnOnLengthMismatch(targetWaits, normalizedChannels.Count, "Target Wait", warn);

            if (poolKeysPerChannel != null && poolKeysPerChannel.Count != normalizedChannels.Count)
            {
                warn?.Invoke(
                    $"Pool Keys has {poolKeysPerChannel.Count} entries but {normalizedChannels.Count} " +
                    "channels are listed; missing entries leave the channel unsplit and extra entries are ignored.");
            }

            for (int i = 0; i < normalizedChannels.Count; i++)
            {
                string channel = normalizedChannels[i];
                int channelPhysical = PickPhysical(physicalResources, i);
                double channelTarget = PickTarget(targetWaits, i);
                IList<string> poolKeys = poolKeysPerChannel != null && i < poolKeysPerChannel.Count
                    ? poolKeysPerChannel[i]
                    : null;

                if (poolKeys == null || poolKeys.Count == 0)
                {
                    plans.Add(new ChannelResourcePlan
                    {
                        Channel = channel,
                        PhysicalResources = channelPhysical,
                        TargetWait95 = channelTarget,
                        Range = defaultBounds
                    });
                    continue;
                }

                HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string poolKey in poolKeys)
                {
                    if (string.IsNullOrWhiteSpace(poolKey))
                        continue;

                    string trimmedKey = poolKey.Trim();
                    if (!IsValidPoolKey(trimmedKey))
                    {
                        warn?.Invoke(
                            $"Pool key '{trimmedKey}' for channel '{channel}' is invalid. " +
                            "Use 'DHPType|ProfileName', for example 'DomesticDeparture|Economy'.");
                        continue;
                    }

                    if (!seenKeys.Add(trimmedKey))
                    {
                        warn?.Invoke(
                            $"Pool key '{trimmedKey}' is listed more than once for channel '{channel}'; duplicates are ignored.");
                        continue;
                    }

                    plans.Add(new ChannelResourcePlan
                    {
                        Channel = channel,
                        PoolKey = trimmedKey,
                        PhysicalResources = channelPhysical,
                        TargetWait95 = channelTarget,
                        Range = defaultBounds
                    });
                }
            }

            ApplyPoolOverrides(plans, poolPhysicalResources, poolTargetWaits,
                poolMinimumResources, poolMaximumResources, defaultBounds, warn);
            return plans;
        }

        private static void ApplyPoolOverrides(
            IList<ChannelResourcePlan> plans,
            IList<int> physical,
            IList<double> targets,
            IList<int> minimums,
            IList<int> maximums,
            ResourceSearchRange defaultBounds,
            Action<string> warn)
        {
            WarnOnExpandedLength(physical, plans.Count, "Pool Nominal Resources", warn);
            WarnOnExpandedLength(targets, plans.Count, "Pool Target Wait", warn);
            WarnOnExpandedLength(minimums, plans.Count, "Pool Minimum Resources", warn);
            WarnOnExpandedLength(maximums, plans.Count, "Pool Maximum Resources", warn);

            for (int i = 0; i < plans.Count; i++)
            {
                ChannelResourcePlan plan = plans[i];
                plan.PhysicalResources = PickPhysicalOverride(physical, i, plans.Count, plan.PhysicalResources);
                plan.TargetWait95 = PickTargetOverride(targets, i, plans.Count, plan.TargetWait95);
                plan.Range = ResolveRange(defaultBounds, minimums, maximums, i, plans.Count, warn);
            }
        }

        private static ResourceSearchRange ResolveRange(
            ResourceSearchRange fallback,
            IList<int> minimums,
            IList<int> maximums,
            int index,
            int planCount,
            Action<string> warn)
        {
            int min = PickBound(minimums, index, planCount, fallback.Min);
            int max = PickBound(maximums, index, planCount, fallback.Max);
            if (max < min)
            {
                warn?.Invoke($"Pool resource maximum {max} is below minimum {min}; maximum was raised to the minimum.");
                max = min;
            }
            return new ResourceSearchRange(min, max).Normalized();
        }

        private static int PickBound(IList<int> values, int index, int count, int fallback)
        {
            if (values == null || values.Count == 0)
                return fallback;
            int slot = values.Count == 1 ? 0 : Math.Min(index, values.Count - 1);
            return Math.Max(1, values[slot]);
        }

        private static int PickPhysicalOverride(IList<int> values, int index, int count, int fallback)
        {
            if (values == null || values.Count == 0)
                return fallback;
            int slot = values.Count == 1 ? 0 : Math.Min(index, values.Count - 1);
            return Math.Max(1, values[slot]);
        }

        private static double PickTargetOverride(IList<double> values, int index, int count, double fallback)
        {
            if (values == null || values.Count == 0)
                return fallback;
            int slot = values.Count == 1 ? 0 : Math.Min(index, values.Count - 1);
            return Math.Max(0.0, values[slot]);
        }

        private static void WarnOnExpandedLength<T>(IList<T> values, int planCount, string label, Action<string> warn)
        {
            if (values == null || values.Count == 0 || values.Count == 1 || values.Count == planCount)
                return;
            warn?.Invoke(
                $"{label} has {values.Count} values but {planCount} pools are resolved; " +
                "a single value broadcasts, otherwise values are applied in order and the last value is reused.");
        }

        private static List<string> NormalizeChannels(IList<string> channels, Action<string> warn)
        {
            List<string> result = new List<string>();
            if (channels == null)
                return result;

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string channel in channels)
            {
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

        private static bool IsValidPoolKey(string poolKey)
        {
            if (string.IsNullOrWhiteSpace(poolKey))
                return false;

            string trimmed = poolKey.Trim();
            int separator = trimmed.IndexOf('|');

            if (separator < 0)
            {
                // Single word: either a valid DHP type (sector split) or a
                // profile name (profile split). Both are valid.
                if (Enum.TryParse(trimmed, true, out DHPType parsedDhp) &&
                    Enum.IsDefined(typeof(DHPType), parsedDhp))
                {
                    return true;  // sector-only split
                }
                else
                {
                    return !string.IsNullOrWhiteSpace(trimmed);  // profile-only split
                }
            }

            string[] parts = trimmed.Split(new[] { '|' }, 2);
            if (parts.Length == 1)
                return false;

            string dhpPart = parts[0].Trim();
            string profilePart = parts.Length > 1 ? parts[1].Trim() : string.Empty;

            if (string.IsNullOrWhiteSpace(profilePart))
                return false;  // must supply profile when using separator

            DHPType dhpType = default;

            if (!string.IsNullOrEmpty(dhpPart) &&
                (!Enum.TryParse(dhpPart, true, out dhpType) ||
                 !Enum.IsDefined(typeof(DHPType), dhpType)))
                return false;

            return true;
        }

        private static void WarnOnLengthMismatch(IList<int> values, int count, string label, Action<string> warn)
        {
            if (values == null || values.Count == 0 || values.Count == 1 || values.Count == count)
                return;
            warn?.Invoke($"{label} has {values.Count} values but {count} channels are listed; the first value is used for every channel.");
        }

        private static void WarnOnLengthMismatch(IList<double> values, int count, string label, Action<string> warn)
        {
            if (values == null || values.Count == 0 || values.Count == 1 || values.Count == count)
                return;
            warn?.Invoke($"{label} has {values.Count} values but {count} channels are listed; the first value is used for every channel.");
        }

        private static int PickPhysical(IList<int> values)
        {
            return values == null || values.Count == 0
                ? DefaultPhysicalResources
                : Math.Max(1, values[0]);
        }

        private static int PickPhysical(IList<int> values, int index)
        {
            return values == null || values.Count == 0
                ? DefaultPhysicalResources
                : Math.Max(1, values[values.Count == 1 ? 0 : Math.Min(index, values.Count - 1)]);
        }

        private static double PickTarget(IList<double> values)
        {
            return values == null || values.Count == 0
                ? DefaultTargetWait95
                : Math.Max(0.0, values[0]);
        }

        private static double PickTarget(IList<double> values, int index)
        {
            return values == null || values.Count == 0
                ? DefaultTargetWait95
                : Math.Max(0.0, values[values.Count == 1 ? 0 : Math.Min(index, values.Count - 1)]);
        }
    }
}
