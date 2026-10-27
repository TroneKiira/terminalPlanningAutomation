using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Queueing
{
    /// <summary>
    /// Resolves a keyed list of <see cref="PoolPlan"/> entries into
    /// <see cref="ChannelResourcePlan"/> plans for the facility solver.
    ///
    /// Resolution rules:
    ///   * A null or empty plan list returns null so the caller can fall back to
    ///     the legacy channel-list resolver.
    ///   * Plans are matched by <see cref="PoolPlan.PoolIdentity"/> ("Channel"
    ///     or "Channel|PoolKey"). Duplicate identities are ignored with a warning.
    ///   * A plan with a null/empty Channel represents the shared pool. If a
    ///     shared plan is present it wins over every channel-based plan.
    ///   * A null <see cref="PoolPlan.Range"/> inherits the fallback domain.
    ///   * The nominal count and target wait are copied from the plan; a missing
    ///     target wait (<= 0) is preserved as "no target" rather than guessed.
    /// </summary>
    public static class PoolPlanResolver
    {
        /// <summary>
        /// Resolves keyed pool plans into channel resource plans. When no plans
        /// are supplied, returns null so the caller can fall back to the legacy
        /// channel-list resolver.
        /// </summary>
        public static List<ChannelResourcePlan> Resolve(
            IList<PoolPlan> plans,
            ResourceSearchRange fallbackDomain,
            Action<string> warn)
        {
            if (plans == null || plans.Count == 0)
                return null;

            ResourceSearchRange resolvedFallback =
                (fallbackDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources)).Normalized();

            List<ChannelResourcePlan> resolved = new List<ChannelResourcePlan>();
            HashSet<string> seenIdentities =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // A shared plan (Channel empty) is exclusive: it replaces every
            // channel-based plan because it serves the whole facility.
            PoolPlan sharedPlan = plans.FirstOrDefault(p =>
                p != null && string.IsNullOrWhiteSpace(p.Channel));

            if (sharedPlan != null)
            {
                resolved.Add(ToChannelPlan(sharedPlan, resolvedFallback));

                foreach (PoolPlan ignored in plans.Where(p =>
                    p != null && !string.IsNullOrWhiteSpace(p.Channel)))
                {
                    warn?.Invoke(
                        $"Pool plan '{ignored.PoolIdentity}' is ignored because a shared-pool plan was supplied.");
                }

                return resolved;
            }

            foreach (PoolPlan plan in plans)
            {
                if (plan == null)
                    continue;

                string identity = plan.PoolIdentity;

                if (string.IsNullOrWhiteSpace(identity))
                {
                    warn?.Invoke("A pool plan has no Channel or PoolKey identity and was ignored.");
                    continue;
                }

                if (!seenIdentities.Add(identity))
                {
                    warn?.Invoke(
                        $"Pool plan '{identity}' is listed more than once; duplicates are ignored.");
                    continue;
                }

                resolved.Add(ToChannelPlan(plan, resolvedFallback));
            }

            return resolved.Count > 0 ? resolved : null;
        }

        /// <summary>
        /// Warns for each resolved pool that received no passengers, which is
        /// usually a key mismatch between the plan and the routed population.
        /// </summary>
        public static void WarnOnEmptyPools(
            IList<ChannelResourcePlan> plans,
            IList<QueueResults> poolResults,
            Action<string> warn)
        {
            if (plans == null || poolResults == null || warn == null)
                return;

            int count = Math.Min(plans.Count, poolResults.Count);

            for (int i = 0; i < count; i++)
            {
                ChannelResourcePlan plan = plans[i];
                QueueResults result = poolResults[i];

                if (plan == null || result == null)
                    continue;

                int processed = result.ProcessedPassengers != null
                    ? result.ProcessedPassengers.Count
                    : 0;

                if (processed == 0)
                {
                    warn($"Pool '{plan.PoolIdentity}' is configured but received no passengers; " +
                         "check the Channel/PoolKey against the routed population.");
                }
            }
        }

        /// <summary>
        /// Warns when a routed channel has no matching plan, which would leave
        /// that channel's passengers unserved. Channel matching is
        /// case-insensitive; subpool plans cover their parent channel.
        /// </summary>
        public static void WarnOnUncoveredChannels(
            IList<ChannelResourcePlan> plans,
            IList<string> channels,
            Action<string> warn)
        {
            if (plans == null || channels == null || warn == null)
                return;

            HashSet<string> covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ChannelResourcePlan plan in plans)
            {
                if (plan != null && !plan.IsSharedPool)
                    covered.Add(plan.Channel);
            }

            foreach (string rawChannel in channels)
            {
                if (string.IsNullOrWhiteSpace(rawChannel))
                    continue;

                string channel = rawChannel.Trim();

                if (!covered.Contains(channel))
                {
                    warn($"Channel '{channel}' has no pool plan; its passengers will not be served.");
                }
            }
        }

        private static ChannelResourcePlan ToChannelPlan(
            PoolPlan plan,
            ResourceSearchRange fallbackDomain)
        {
            ResourceSearchRange range = plan.Range;

            if (range == null)
                range = fallbackDomain;
            else
                range = range.Normalized();

            return new ChannelResourcePlan
            {
                Channel = plan.Channel,
                PoolKey = plan.PoolKey,
                PhysicalResources = plan.PhysicalResources < 1 ? 1 : plan.PhysicalResources,
                TargetWait95 = plan.TargetWait95 <= 0.0 ? 0.0 : plan.TargetWait95,
                Range = range
            };
        }
    }
}
