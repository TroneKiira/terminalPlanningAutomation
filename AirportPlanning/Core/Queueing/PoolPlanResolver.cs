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
        ///
        /// When <paramref name="facility"/> is supplied, rows tagged with a
        /// different facility are ignored and a row tagged with this facility
        /// wins over an untagged row with the same identity. That lets one
        /// shared list carry, say, an Emigration "EGate" plan and a Security
        /// "EGate" plan with different values.
        /// </summary>
        public static List<ChannelResourcePlan> Resolve(
            IList<PoolPlan> plans,
            ResourceSearchRange fallbackDomain,
            Action<string> warn,
            string facility = null)
        {
            if (plans == null || plans.Count == 0)
                return null;

            string scope = string.IsNullOrWhiteSpace(facility) ? null : facility.Trim();

            ResourceSearchRange resolvedFallback =
                (fallbackDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources)).Normalized();

            // Tags are matched leniently (case, spacing, planning-table
            // spellings); a tag that matches no facility at all is reported
            // rather than silently dropped, because a dropped row silently
            // reverts the facility to its fallback plans.
            HashSet<string> unknownTags =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            List<PoolPlan> matching = new List<PoolPlan>();

            foreach (PoolPlan plan in plans)
            {
                if (plan == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(plan.Facility))
                {
                    if (FacilityName.Canonicalize(plan.Facility) == null)
                    {
                        if (unknownTags.Add(plan.Facility.Trim()))
                        {
                            warn?.Invoke(
                                $"Pool plan row '{plan.PoolIdentity}' is tagged with facility " +
                                $"'{plan.Facility.Trim()}', which matches no facility. Valid names: " +
                                $"{FacilityName.DescribeValid()}. The row is ignored.");
                        }

                        continue;
                    }

                    if (scope != null &&
                        !FacilityName.Matches(plan.Facility, scope))
                    {
                        continue;
                    }
                }

                matching.Add(plan);
            }

            if (matching.Count == 0)
            {
                if (scope != null)
                {
                    List<string> otherTags = new List<string>();

                    foreach (PoolPlan plan in plans)
                    {
                        if (plan == null || string.IsNullOrWhiteSpace(plan.Facility))
                            continue;

                        string tag = plan.Facility.Trim();

                        if (!otherTags.Contains(tag))
                            otherTags.Add(tag);
                    }

                    warn?.Invoke(
                        $"No pool plan belongs to '{scope}'. Rows exist for: " +
                        $"{(otherTags.Count > 0 ? string.Join(", ", otherTags) : "(untagged rows only?)")}. " +
                        $"Valid facility names: {FacilityName.DescribeValid()}. " +
                        "The facility falls back to its legacy channel inputs.");
                }

                return null;
            }

            List<ChannelResourcePlan> resolved = new List<ChannelResourcePlan>();

            // A shared plan (Channel empty) is exclusive: it replaces every
            // channel-based plan because it serves the whole facility. A
            // facility-specific shared plan wins over a blank one.
            PoolPlan sharedPlan = PickPreferred(
                matching.Where(p => string.IsNullOrWhiteSpace(p.Channel)).ToList(),
                scope,
                warn,
                shared: true);

            if (sharedPlan != null)
            {
                resolved.Add(ToChannelPlan(sharedPlan, resolvedFallback));

                foreach (PoolPlan ignored in matching.Where(p =>
                    !string.IsNullOrWhiteSpace(p.Channel)))
                {
                    warn?.Invoke(
                        $"Pool plan '{ignored.PoolIdentity}' is ignored because a shared-pool plan was supplied.");
                }

                return resolved;
            }

            Dictionary<string, PoolPlan> chosen =
                new Dictionary<string, PoolPlan>(StringComparer.OrdinalIgnoreCase);

            foreach (PoolPlan plan in matching)
            {
                string identity = plan.PoolIdentity;

                if (string.IsNullOrWhiteSpace(identity))
                {
                    warn?.Invoke("A pool plan has no Channel or PoolKey identity and was ignored.");
                    continue;
                }

                // In scoped mode the duplicate key is the identity alone, so a
                // facility-tagged row can win over an untagged row with the
                // same identity. In unscoped (legacy) mode the facility tag
                // joins the key, so the same identity at two facilities stays
                // two distinct pools.
                string dedupKey = scope != null
                    ? identity
                    : (string.IsNullOrWhiteSpace(plan.Facility)
                        ? string.Empty
                        : plan.Facility.Trim()) + "|" + identity;

                if (!chosen.TryGetValue(dedupKey, out PoolPlan existing))
                {
                    chosen[dedupKey] = plan;
                    continue;
                }

                // A facility-specific row beats an untagged row for the same
                // identity. Two rows of the same specificity are a duplicate.
                bool incomingSpecific = IsSpecific(plan, scope);
                bool existingSpecific = IsSpecific(existing, scope);

                if (incomingSpecific && !existingSpecific)
                {
                    chosen[dedupKey] = plan;
                    continue;
                }

                warn?.Invoke(
                    $"Pool plan '{identity}' is listed more than once; duplicates are ignored.");
            }

            foreach (PoolPlan plan in chosen.Values)
                resolved.Add(ToChannelPlan(plan, resolvedFallback));

            return resolved.Count > 0 ? resolved : null;
        }

        private static PoolPlan PickPreferred(
            List<PoolPlan> candidates,
            string scope,
            Action<string> warn,
            bool shared)
        {
            if (candidates == null || candidates.Count == 0)
                return null;

            PoolPlan chosen = null;

            foreach (PoolPlan candidate in candidates)
            {
                if (chosen == null)
                {
                    chosen = candidate;
                    continue;
                }

                if (IsSpecific(candidate, scope) && !IsSpecific(chosen, scope))
                {
                    chosen = candidate;
                    continue;
                }

                warn?.Invoke(shared
                    ? "More than one shared-pool plan was supplied; duplicates are ignored."
                    : $"Pool plan '{candidate.PoolIdentity}' is listed more than once; duplicates are ignored.");
            }

            return chosen;
        }

        private static bool IsSpecific(PoolPlan plan, string scope)
        {
            return plan != null &&
                   scope != null &&
                   !string.IsNullOrWhiteSpace(plan.Facility) &&
                   FacilityName.Matches(plan.Facility, scope);
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
            WarnOnEmptyPools(plans, poolResults, null, warn);
        }

        /// <summary>
        /// Warns for each resolved pool that received no passengers. When the
        /// routed population is supplied, the warning lists the identities the
        /// population actually carries - "Channel|DHPType|ProfileName" strings
        /// in the same grammar the Pool Plans use - so a key mismatch is
        /// readable straight from the message.
        /// </summary>
        public static void WarnOnEmptyPools(
            IList<ChannelResourcePlan> plans,
            IList<QueueResults> poolResults,
            IEnumerable<Passenger> population,
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
                         "check the Channel/PoolKey against the routed population." +
                         DescribePopulation(population, plan));
                }
            }
        }

        /// <summary>
        /// The identities the routed population carries, as
        /// "Channel|DHPType|ProfileName" strings - the same grammar a Pool Plan
        /// identity uses - capped at twelve lines so the message stays readable.
        /// Omitted when no population was supplied or the empty pool is the
        /// shared pool (which serves everyone and can only be empty because the
        /// stage filtered everyone out).
        /// </summary>
        private static string DescribePopulation(
            IEnumerable<Passenger> population,
            ChannelResourcePlan emptyPlan)
        {
            if (population == null || (emptyPlan != null && emptyPlan.IsSharedPool))
                return string.Empty;

            List<string> identities = population
                .Where(p => p != null)
                .Select(p => string.Join("|",
                    string.IsNullOrWhiteSpace(p.Channel) ? "(no channel)" : p.Channel.Trim(),
                    p.DHPType.ToString(),
                    string.IsNullOrWhiteSpace(p.ProfileName) ? "(no profile)" : p.ProfileName.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (identities.Count == 0)
                return " The routed population is empty.";

            int shown = Math.Min(12, identities.Count);

            return $" The routed population carries {identities.Count} identit{(identities.Count == 1 ? "y" : "ies")}: " +
                   string.Join("; ", identities.Take(shown)) +
                   (identities.Count > shown ? "; ..." : string.Empty) + ".";
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
