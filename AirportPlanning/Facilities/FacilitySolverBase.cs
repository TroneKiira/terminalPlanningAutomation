using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Distributions;

using AirportPlanning.Core.Walking;


namespace AirportPlanning.Facilities
{
    public abstract class FacilitySolverBase
    {
        protected readonly LogNormalServiceTimeGenerator _serviceGenerator;
        protected readonly QueueSimulator _queueSimulator;
        protected readonly QueueMetricsCalculator _metricsCalculator;

        /// <summary>Reusable optimizer; stateless and therefore thread safe.</summary>
        protected readonly QueueOptimizer _optimizer;

        /// <summary>
        /// Validation problems from the most recent <c>Solve</c>. The host
        /// component drains this and re-raises them as Grasshopper warnings.
        /// </summary>
        public List<string> Warnings { get; } = new List<string>();

        protected FacilitySolverBase()
        {
            _serviceGenerator = new LogNormalServiceTimeGenerator();
            _queueSimulator = new QueueSimulator();
            _metricsCalculator = new QueueMetricsCalculator();
            _optimizer = new QueueOptimizer();
        }

        /// <summary>
        /// Aggregate outcome of one facility solve. When the facility serves a
        /// single shared pool this collapses to the legacy shape; with dedicated
        /// channel pools the per-pool detail is kept alongside the totals.
        /// </summary>
        protected class FacilitySolveResult
        {
            public QueueResults Results { get; set; }

            public List<Passenger> Processed { get; set; } = new List<Passenger>();

            public List<QueueResource> AllResources { get; set; } = new List<QueueResource>();

            public List<int> PhysicalPerChannel { get; set; } = new List<int>();

            public List<int> ActivePerChannel { get; set; } = new List<int>();

            public Dictionary<string, double> Wait95PerChannel { get; set; } =
                new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            public List<QueueResults> PoolResults { get; set; } = new List<QueueResults>();

            /// <summary>
            /// Pool names in <see cref="PoolResults"/> order: the dedicated
            /// channel name, or "Shared" for a single facility-wide queue.
            /// </summary>
            public List<string> PoolNames { get; set; } = new List<string>();

            /// <summary>
            /// True for each pool that serves every passenger in one queue.
            /// </summary>
            public List<bool> PoolIsShared { get; set; } = new List<bool>();

            /// <summary>
            /// Target Wait95 per pool, in <see cref="PoolResults"/> order. Kept so
            /// the merged result can report whether each pool met its own target
            /// without re-reading the optimizer outcomes (optimization may be off).
            /// </summary>
            public List<double> TargetPerPool { get; set; } = new List<double>();

            public List<ResourceSearchOutcome> SearchOutcomes { get; set; } = new List<ResourceSearchOutcome>();

            /// <summary>
            /// Maps each upstream passenger this facility actually served to the
            /// private clone the simulation mutated. Used to rebuild the outgoing
            /// population without ever handing the input list back to
            /// Grasshopper.
            /// </summary>
            public Dictionary<Passenger, Passenger> Clones { get; } =
                new Dictionary<Passenger, Passenger>();

            public int TotalPhysical { get; set; }

            public int TotalActive { get; set; }

            public bool BaselinePhysical { get; set; }
        }

        /// <summary>
        /// Builds the outgoing passenger population.
        ///
        /// Every passenger this facility served is replaced by the clone the
        /// simulation mutated (so service history and the advanced clock travel
        /// downstream), and everyone else is passed through untouched. The
        /// returned list is always a fresh instance: returning the very list
        /// Grasshopper handed in would alias the component's own input, which
        /// stops solution diffing from settling and re-solves the document forever.
        /// </summary>
        protected static List<Passenger> BuildOutputPopulation(
            IEnumerable<Passenger> passengers,
            Dictionary<Passenger, Passenger> clones)
        {
            List<Passenger> output = new List<Passenger>();

            if (passengers == null)
                return output;

            foreach (Passenger passenger in passengers)
            {
                if (passenger != null &&
                    clones != null &&
                    clones.TryGetValue(passenger, out Passenger clone))
                {
                    output.Add(clone);
                }
                else
                {
                    output.Add(passenger);
                }
            }

            return output;
        }

        /// <summary>
        /// True when the passenger is a departing passenger (Domestic or
        /// International Departure). Arrival passengers never use Check-In,
        /// Bag Drop, Security or Emigration.
        /// </summary>
        public static bool IsDeparturePassenger(Passenger passenger)
        {
            if (passenger == null)
                return false;

            return passenger.DHPType == Core.Inputs.DHPType.DomesticDeparture ||
                   passenger.DHPType == Core.Inputs.DHPType.InternationalDeparture;
        }

        /// <summary>
        /// Builds the passenger filter for one pool. A shared pool keeps the
        /// component's channel filter (or the legacy predicate when no channel is
        /// wired); a dedicated pool keeps only its own channel. When the plan has
        /// a <see cref="ChannelResourcePlan.PoolKey"/>, the pool is further split
        /// by DHPType + ProfileName (the key format is "DHPType|ProfileName").
        /// A required predicate (e.g. departures-only) is always applied on top,
        /// so wiring a channel can never let excluded passengers through.
        /// Set <paramref name="matchPrimaryChannel"/> to false for a stage such
        /// as Bag Drop, whose passengers are selected by their service
        /// requirement rather than by their primary check-in channel.
        /// </summary>
        protected static Func<Passenger, bool> BuildPlanFilter(
            ChannelResourcePlan plan,
            List<string> channels,
            Func<Passenger, bool> legacyPredicate,
            Func<Passenger, bool> requiredPredicate = null,
            bool matchPrimaryChannel = true,
            Func<Passenger, bool> sharedPoolFilter = null)
        {
            Func<Passenger, bool> baseFilter;

            if (plan == null || plan.IsSharedPool)
            {
                if (sharedPoolFilter != null)
                {
                    // A facility can define its own shared-pool population, for
                    // example Bag Drop's fallback pool takes the eligible
                    // passengers whose segment no keyed plan covers.
                    baseFilter = sharedPoolFilter;
                }
                else
                {
                    baseFilter = matchPrimaryChannel
                        ? BuildChannelFilter(channels, legacyPredicate)
                        : legacyPredicate;
                }
            }
            else
            {
                string channel = plan.Channel;
                string poolKey = plan.PoolKey;

                if (!string.IsNullOrWhiteSpace(poolKey))
                {
                    (DHPType? dhpType, string profileName) = ParsePoolKey(poolKey);

                    // C# 7.3 (the net48 target) rejects a conditional over two
                    // lambdas that need separate conversions, so the branches
                    // are written out explicitly.
                    if (matchPrimaryChannel)
                    {
                        baseFilter = p => p != null &&
                            string.Equals(p.Channel, channel, StringComparison.OrdinalIgnoreCase) &&
                            MatchesPoolKey(p, dhpType, profileName);
                    }
                    else
                    {
                        baseFilter = p => p != null && MatchesPoolKey(p, dhpType, profileName);
                    }
                }
                else
                {
                    if (matchPrimaryChannel)
                    {
                        baseFilter = p =>
                            p != null &&
                            string.Equals(p.Channel, channel, StringComparison.OrdinalIgnoreCase);
                    }
                    else
                    {
                        baseFilter = p => p != null;
                    }
                }
            }

            if (requiredPredicate == null)
                return baseFilter;

            return p => p != null && requiredPredicate(p) && baseFilter(p);
        }

        /// <summary>
        /// Parses a pool key of the form "DHPType|ProfileName" into its
        /// components. A missing or unparseable DHPType yields null (no DHP
        /// constraint); a missing profile name yields an empty string (no
        /// profile constraint).
        /// </summary>
        private static (DHPType? DhpType, string ProfileName) ParsePoolKey(string poolKey)
        {
            if (string.IsNullOrWhiteSpace(poolKey))
                return (null, string.Empty);

            string trimmed = poolKey.Trim();
            int separator = trimmed.IndexOf('|');

            // Try single-word forms first: a valid DHP type = sector split,
            // anything else = profile split.
            if (separator < 0)
            {
                if (Enum.TryParse(trimmed, true, out DHPType parsedDhp) &&
                    Enum.IsDefined(typeof(DHPType), parsedDhp))
                {
                    // Sector-only split: DHP type applies, no profile constraint.
                    return (parsedDhp, string.Empty);
                }
                else
                {
                    // Profile-only split: profile applies, no DHP constraint.
                    return (null, trimmed);
                }
            }

            string dhpPart = trimmed.Substring(0, separator).Trim();
            string profilePart = trimmed.Substring(separator + 1).Trim();

            DHPType? dhpType = null;

            if (!string.IsNullOrEmpty(dhpPart) &&
                Enum.TryParse(dhpPart, true, out DHPType parsed))
            {
                dhpType = Enum.IsDefined(typeof(DHPType), parsed) ? parsed : (DHPType?)null;
            }

            return (dhpType, profilePart);
        }

        /// <summary>
        /// True when the passenger matches the pool key's DHP type and/or
        /// profile name. A null DHP type or empty profile name imposes no
        /// constraint on that dimension.
        /// </summary>
        private static bool MatchesPoolKey(
            Passenger passenger,
            DHPType? dhpType,
            string profileName)
        {
            if (passenger == null)
                return false;

            if (dhpType.HasValue && passenger.DHPType != dhpType.Value)
                return false;

            if (!string.IsNullOrEmpty(profileName) &&
                !string.Equals(passenger.ProfileName, profileName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// True when any keyed plan's segment covers the passenger. A keyed
        /// plan with a pool key covers its DHPType/ProfileName slice; a keyed
        /// plan without a pool key is facility-wide and covers everyone. Used
        /// by facilities whose keyed pools are segment queues, to decide who
        /// still needs the shared fallback pool.
        /// </summary>
        protected static bool MatchesAnyKeyedSegment(
            Passenger passenger,
            IEnumerable<ChannelResourcePlan> keyedPlans)
        {
            if (passenger == null || keyedPlans == null)
                return false;

            foreach (ChannelResourcePlan plan in keyedPlans)
            {
                if (plan == null || plan.IsSharedPool)
                    continue;

                if (string.IsNullOrWhiteSpace(plan.PoolKey))
                    return true;

                (DHPType? dhpType, string profileName) = ParsePoolKey(plan.PoolKey);

                if (MatchesPoolKey(passenger, dhpType, profileName))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Sizes and simulates every pool of a facility.
        ///
        /// Service times are assigned once per pool from the seed, so all
        /// candidate capacities see identical demand and durations and the search
        /// stays deterministic. Only a Wait95 is computed while searching; the
        /// full metrics object is built once per pool at the chosen capacity.
        /// </summary>
        protected FacilitySolveResult SolvePools(
            IEnumerable<Passenger> passengers,
            IList<ChannelResourcePlan> plans,
            List<string> channels,
            Func<Passenger, bool> legacyPredicate,
            IServiceTimeProvider serviceTimeProvider,
            int seed,
            bool optimize,
            double staffingPercentage,
            List<ResourceSearchOutcome> cachedOutcomes,
            out List<ResourceSearchOutcome> outcomesUsed,
            Action<string> warn,
            Func<Passenger, bool> requiredPredicate = null,
            bool matchPrimaryChannel = true,
            Func<Passenger, bool> sharedPoolFilter = null)
        {
            FacilitySolveResult solve = new FacilitySolveResult();

            outcomesUsed = null;

            if (plans == null || plans.Count == 0)
            {
                solve.Results = CreateEmptyResult(new List<Passenger>());
                return solve;
            }

            List<List<Passenger>> pools = new List<List<Passenger>>(plans.Count);

            Dictionary<ChannelResourcePlan, int> poolIndex =
                new Dictionary<ChannelResourcePlan, int>();

            for (int i = 0; i < plans.Count; i++)
            {
                int designStep;

                List<Passenger> pool = BuildPool(
                    passengers,
                    BuildPlanFilter(
                        plans[i],
                        channels,
                        legacyPredicate,
                        requiredPredicate,
                        matchPrimaryChannel,
                        sharedPoolFilter),
                    serviceTimeProvider,
                    seed,
                    solve.Clones,
                    out designStep);

                poolIndex[plans[i]] = i;

                pools.Add(pool);
            }

            List<ResourceSearchOutcome> chosen = null;

            if (optimize)
            {
                if (cachedOutcomes != null && cachedOutcomes.Count == plans.Count)
                {
                    chosen = cachedOutcomes;
                }
                else
                {
                    chosen = _optimizer.Optimize(
                        plans,
                        staffingPercentage,
                        (plan, physical) =>
                        {
                            int index = poolIndex[plan];
                            int active = ChannelResourcePlan.ToActiveResources(physical, staffingPercentage);

                            // The target applies to the whole-run W95 shown in
                            // the outputs, not to the design-hour step alone.
                            return EvaluateCapacity(pools[index], active);
                        },
                        warn);
                }

                outcomesUsed = chosen;
                solve.SearchOutcomes = chosen ?? new List<ResourceSearchOutcome>();

                WarnOnMissedTargets(chosen, warn);
            }

            for (int i = 0; i < plans.Count; i++)
            {
                ChannelResourcePlan plan = plans[i];

                int physical = plan.PhysicalResources;
                int active = ChannelResourcePlan.ToActiveResources(physical, staffingPercentage);

                if (chosen != null && i < chosen.Count && chosen[i] != null)
                {
                    physical = chosen[i].OptimalPhysicalResources;
                    active = chosen[i].OptimalActiveResources;
                }

                solve.PhysicalPerChannel.Add(physical);
                solve.ActivePerChannel.Add(active);
                solve.TotalPhysical += physical;
                solve.TotalActive += active;

                string poolName = plan.IsSharedPool ? "Shared" : plan.PoolIdentity;

                solve.PoolNames.Add(poolName);
                solve.PoolIsShared.Add(plan.IsSharedPool);
                solve.TargetPerPool.Add(plan.TargetWait95);

                if (!plan.IsSharedPool)
                    solve.Wait95PerChannel[plan.PoolIdentity] = 0.0;

                if (pools[i].Count == 0)
                {
                    solve.PoolResults.Add(CreateEmptyResult(pools[i]));
                    continue;
                }

                QueueResults poolResults = FinalizePool(pools[i], active);

                AssignPoolIdentity(poolResults.ProcessedPassengers, poolName);

                solve.PoolResults.Add(poolResults);
                solve.Processed.AddRange(poolResults.ProcessedPassengers);
                solve.AllResources.AddRange(poolResults.Resources);

                if (!plan.IsSharedPool)
                    solve.Wait95PerChannel[plan.PoolIdentity] = poolResults.Wait95;
            }

            solve.Results = MergePoolResults(solve);

            return solve;
        }

        /// <summary>
        /// Rolls the per-pool results into one facility-level result. With a
        /// single shared pool the pool result is returned untouched, which keeps
        /// the legacy numbers identical.
        /// </summary>
        protected QueueResults MergePoolResults(FacilitySolveResult solve)
        {
            List<ProcessorPoolInfo> processorPools = BuildProcessorPools(solve);
            List<QueuePoolResult> queuePools = BuildQueuePoolResults(solve, processorPools);

            if (solve.PoolResults.Count == 1)
            {
                QueueResults only = solve.PoolResults[0];

                only.ProcessedPassengers = solve.Processed;
                only.Resources = solve.AllResources;
                only.ResourcesUsed = solve.AllResources.Count;
                only.ProcessorPools = processorPools;
                only.PoolResults = queuePools;

                return only;
            }

            List<Passenger> combined = new List<Passenger>();

            foreach (QueueResults pool in solve.PoolResults)
            {
                if (pool?.ProcessedPassengers != null)
                    combined.AddRange(pool.ProcessedPassengers);
            }

            QueueResults merged = _metricsCalculator.Calculate(
                combined, new List<QueueResource>());

            merged.ProcessedPassengers = combined;
            merged.Resources = solve.AllResources;
            merged.ResourcesUsed = solve.AllResources.Count;
            merged.ProcessorPools = processorPools;
            merged.PoolResults = queuePools;

            return merged;
        }

        /// <summary>
        /// Copies each simulated pool's own queue history into the merged
        /// result. The merged timeline is the facility aggregate; these
        /// snapshots are the independent channel/sector queues behind it.
        /// </summary>
        private static List<QueuePoolResult> BuildQueuePoolResults(
            FacilitySolveResult solve,
            List<ProcessorPoolInfo> processorPools)
        {
            List<QueuePoolResult> pools = new List<QueuePoolResult>();

            for (int i = 0; i < solve.PoolResults.Count; i++)
            {
                QueueResults pool = solve.PoolResults[i];

                string name = i < solve.PoolNames.Count &&
                              !string.IsNullOrWhiteSpace(solve.PoolNames[i])
                    ? solve.PoolNames[i]
                    : (solve.PoolResults.Count == 1 ? "Shared" : $"Pool {i + 1}");

                ProcessorPoolInfo info = i < processorPools.Count
                    ? processorPools[i]
                    : null;

                SplitPoolIdentity(name, out string channel, out string poolKey);

                List<Passenger> served = pool?.ProcessedPassengers != null
                    ? pool.ProcessedPassengers
                        .Where(p => p != null && !p.IsWarmUp)
                        .ToList()
                    : new List<Passenger>();

                List<QueueTimelinePoint> queueEvents = pool?.QueueTimeline != null
                    ? pool.QueueTimeline
                        .Where(p => p != null)
                        .OrderBy(p => p.Time)
                        .ToList()
                    : new List<QueueTimelinePoint>();

                List<QueueCumulativePoint> cumulativeFlow = pool?.CumulativeFlow != null
                    ? new List<QueueCumulativePoint>(pool.CumulativeFlow)
                    : new List<QueueCumulativePoint>();

                pools.Add(new QueuePoolResult
                {
                    Name = name,
                    Channel = channel,
                    PoolKey = poolKey,
                    IsShared = i < solve.PoolIsShared.Count
                        ? solve.PoolIsShared[i]
                        : string.Equals(name, "Shared", StringComparison.OrdinalIgnoreCase),
                    PhysicalResources = info != null ? info.PhysicalResourceCount : 0,
                    ActiveResources = info != null ? info.ResourceCount : 0,
                    TargetWait95 = i < solve.TargetPerPool.Count
                        ? solve.TargetPerPool[i]
                        : 0.0,
                    PassengerCount = served.Count,
                    WaitingPassengerCount = served.Count(p => p.WaitTime > 1e-9),
                    FirstArrivalTime = served.Count > 0
                        ? served.Min(p => p.ArrivalTime)
                        : double.NaN,
                    LastArrivalTime = served.Count > 0
                        ? served.Max(p => p.ArrivalTime)
                        : double.NaN,
                    FirstQueueEventTime = queueEvents.Count > 0
                        ? queueEvents[0].Time
                        : double.NaN,
                    LastQueueEventTime = queueEvents.Count > 0
                        ? queueEvents[queueEvents.Count - 1].Time
                        : double.NaN,
                    AverageWait = pool != null ? pool.AverageWait : 0.0,
                    MaxWait = pool != null ? pool.MaxWait : 0.0,
                    Wait95 = pool != null ? pool.Wait95 : 0.0,
                    PeakQueueLength = pool != null ? pool.PeakQueueLength : 0,
                    PeakQueueTime = pool != null ? pool.PeakQueueTime : 0.0,
                    SimulationDuration = pool != null ? pool.SimulationDuration : 0.0,
                    QueueTimeline = queueEvents,
                    CumulativeFlow = cumulativeFlow
                });
            }

            return pools;
        }

        /// <summary>
        /// Splits "Channel|DHPType|ProfileName" into its channel and pool-key
        /// halves. A bare channel yields an empty key; "Shared" yields neither.
        /// </summary>
        private static void SplitPoolIdentity(
            string identity,
            out string channel,
            out string poolKey)
        {
            channel = string.Empty;
            poolKey = string.Empty;

            if (string.IsNullOrWhiteSpace(identity) ||
                string.Equals(identity, "Shared", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int separator = identity.IndexOf('|');

            if (separator < 0)
            {
                channel = identity.Trim();
                return;
            }

            channel = identity.Substring(0, separator).Trim();
            poolKey = identity.Substring(separator + 1).Trim();
        }

        /// <summary>
        /// Tags every passenger a pool served with that pool's identity, so
        /// downstream charts can keep pools that share a channel apart. The
        /// tag is assigned after simulation because queue resets clear it.
        /// </summary>
        private static void AssignPoolIdentity(
            IEnumerable<Passenger> passengers,
            string poolIdentity)
        {
            if (passengers == null)
                return;

            foreach (Passenger passenger in passengers)
            {
                if (passenger != null)
                    passenger.AssignedPool = poolIdentity ?? string.Empty;
            }
        }

        /// <summary>
        /// Describes the resource pools merged into the facility result. The
        /// resource offset is cumulative so a dedicated pool's processor IDs
        /// can be traced back to the merged resource list.
        /// </summary>
        private static List<ProcessorPoolInfo> BuildProcessorPools(FacilitySolveResult solve)
        {
            List<ProcessorPoolInfo> pools = new List<ProcessorPoolInfo>();

            int offset = 0;

            for (int i = 0; i < solve.PoolResults.Count; i++)
            {
                QueueResults pool = solve.PoolResults[i];

                int count = pool != null && pool.Resources != null
                    ? pool.Resources.Count
                    : 0;

                string name = i < solve.PoolNames.Count &&
                              !string.IsNullOrWhiteSpace(solve.PoolNames[i])
                    ? solve.PoolNames[i]
                    : (solve.PoolResults.Count == 1 ? "Shared" : $"Pool {i + 1}");

                bool isShared = i < solve.PoolIsShared.Count
                    ? solve.PoolIsShared[i]
                    : solve.PoolResults.Count == 1;

                int physical = i < solve.PhysicalPerChannel.Count
                    ? solve.PhysicalPerChannel[i]
                    : count;

                pools.Add(new ProcessorPoolInfo
                {
                    Name = name,
                    ResourceCount = count,
                    PhysicalResourceCount = physical,
                    FirstResourceIndex = offset,
                    IsShared = isShared
                });

                offset += count;
            }

            return pools;
        }

        /// <summary>
        /// Records this facility's visit on every processed passenger.
        /// </summary>
        protected static void WriteFacilityHistory(
            IEnumerable<Passenger> processed,
            FacilityType facility)
        {
            if (processed == null)
                return;

            foreach (Passenger passenger in processed)
            {
                if (passenger == null)
                    continue;

                passenger.History[facility] = new FacilityRecord
                {
                    ArrivalTime = passenger.ArrivalTime,
                    StartServiceTime = passenger.StartServiceTime,
                    EndServiceTime = passenger.EndServiceTime,
                    WaitTime = passenger.WaitTime,
                    AssignedResource = passenger.AssignedCounter,
                    PoolName = passenger.AssignedPool ?? string.Empty
                };
            }
        }

        protected List<Passenger> FilterPassengers(
            IEnumerable<Passenger> passengers,
            Func<Passenger, bool> predicate)
        {
            // FIX: Explicitly sort passengers chronologically to prevent Time-Travel bugs!
            return passengers
                .Where(predicate)
                .OrderBy(p => p.ArrivalTime)
                .ToList();
        }

        /// <summary>
        /// Compact fingerprint of a passenger population's windowed demand tags.
        /// Used in optimizer cache keys so design-step or warm-up changes force a
        /// re-optimization.
        /// </summary>
        protected static string GetDemandSignature(IEnumerable<Passenger> passengers)
        {
            if (passengers == null)
                return "null";

            int designStep = -1;
            int warmUpCount = 0;
            int count = 0;

            foreach (Passenger passenger in passengers)
            {
                count++;

                if (passenger.IsDesignStep && designStep < 0)
                    designStep = passenger.DemandStepIndex;

                if (passenger.IsWarmUp)
                    warmUpCount++;
            }

            return $"{designStep}_{warmUpCount}_{count}";
        }

        /// <summary>
        /// Compact fingerprint of the channels present in a population and their
        /// counts. Used in optimizer cache keys so a routing change forces a
        /// re-optimization.
        /// </summary>
        protected static string GetChannelSignature(IEnumerable<Passenger> passengers)
        {
            if (passengers == null)
                return "null";

            SortedDictionary<string, int> counts =
                new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (Passenger passenger in passengers)
            {
                string channel = string.IsNullOrEmpty(passenger.Channel) ? "(none)" : passenger.Channel;

                counts.TryGetValue(channel, out int current);
                counts[channel] = current + 1;
            }

            StringBuilder builder = new StringBuilder();

            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (builder.Length > 0)
                    builder.Append('|');

                builder.Append(pair.Key);
                builder.Append(':');
                builder.Append(pair.Value);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Compact fingerprint of the show-up curves that shaped a population.
        /// Used in optimizer cache keys so switching the show-up profile (e.g.
        /// Leisure to Business) forces a fresh search even when the realized
        /// arrival times happen to look similar.
        /// </summary>
        protected static string GetShowUpSignature(IEnumerable<Passenger> passengers)
        {
            if (passengers == null)
                return "null";

            SortedDictionary<string, int> counts =
                new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (Passenger passenger in passengers)
            {
                string signature = passenger == null || string.IsNullOrEmpty(passenger.ShowUpSignature)
                    ? "none"
                    : passenger.ShowUpSignature;

                counts.TryGetValue(signature, out int current);
                counts[signature] = current + 1;
            }

            StringBuilder builder = new StringBuilder();

            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (builder.Length > 0)
                    builder.Append('|');

                builder.Append(pair.Key);
                builder.Append(':');
                builder.Append(pair.Value);
            }

            return builder.Length == 0 ? "none" : builder.ToString();
        }

        /// <summary>
        /// Deterministic hash of the realized arrival schedule and per-passenger
        /// demand tags. Used in optimizer cache keys so any change to arrival
        /// timing forces a fresh search. Order-independent (passengers are
        /// ordered by Id before hashing) and compact regardless of population
        /// size.
        /// </summary>
        protected static string GetScheduleSignature(IEnumerable<Passenger> passengers)
        {
            if (passengers == null)
                return "null";

            List<Passenger> ordered = passengers
                .Where(p => p != null)
                .OrderBy(p => p.Id)
                .ToList();

            // FNV-1a 64-bit over the per-passenger schedule and check-in fields.
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong hash = offsetBasis;

            foreach (Passenger passenger in ordered)
            {
                ulong bits = (ulong)BitConverter.DoubleToInt64Bits(passenger.ArrivalTime);
                hash ^= bits;
                hash *= prime;

                bits = (ulong)BitConverter.DoubleToInt64Bits(passenger.QueueArrivalTime);
                hash ^= bits;
                hash *= prime;

                bits = (ulong)BitConverter.DoubleToInt64Bits(passenger.CheckInOpenTime);
                hash ^= bits;
                hash *= prime;

                bits = (ulong)BitConverter.DoubleToInt64Bits(passenger.CheckInLastAdmissionTime);
                hash ^= bits;
                hash *= prime;

                hash ^= passenger.IsLateForCheckIn ? 1UL : 0UL;
                hash *= prime;

                hash ^= (uint)passenger.DHPType;
                hash *= prime;

                hash ^= (uint)passenger.Journey;
                hash *= prime;

                hash ^= passenger.TransferScenario.HasValue
                    ? (uint)passenger.TransferScenario.Value + 1U
                    : 0U;
                hash *= prime;

                if (passenger.TransferProcessing == null)
                {
                    hash ^= 0UL;
                }
                else
                {
                    TransferScenarioRules rules = passenger.TransferProcessing;
                    ulong flags = 1UL;
                    if (rules.RequiresTransferDesk) flags |= 1UL << 7;
                    if (rules.RequiresSecurityScreening) flags |= 1UL << 1;
                    if (rules.RequiresImmigration) flags |= 1UL << 2;
                    if (rules.RequiresEmigration) flags |= 1UL << 3;
                    if (rules.RequiresBaggageRecheck) flags |= 1UL << 4;
                    if (rules.RequiresCheckIn) flags |= 1UL << 5;
                    if (rules.RequiresBagDrop) flags |= 1UL << 6;
                    if (rules.SecurityRouting == TransferFacilityRouting.Separate) flags |= 1UL << 8;
                    if (rules.EmigrationRouting == TransferFacilityRouting.Separate) flags |= 1UL << 9;
                    if (rules.ImmigrationRouting == TransferFacilityRouting.Separate) flags |= 1UL << 10;
                    hash ^= flags;
                }
                hash *= prime;

                hash ^= (uint)(passenger.DemandStepIndex + 2);
                hash *= prime;

                hash ^= (uint)(passenger.DemandHourIndex + 2);
                hash *= prime;

                hash ^= passenger.IsWarmUp ? 1UL : 0UL;
                hash *= prime;

                hash ^= passenger.IsDesignStep ? 1UL : 0UL;
                hash *= prime;
            }

            return $"{ordered.Count}_{hash:X16}";
        }

        /// <summary>
        /// Compact fingerprint of the passenger profiles present in a population
        /// and their counts. Used in optimizer cache keys so a profile-mix
        /// change forces a re-optimization even when channel counts and the
        /// service-time map are untouched.
        /// </summary>
        protected static string GetProfileSignature(IEnumerable<Passenger> passengers)
        {
            if (passengers == null)
                return "null";

            SortedDictionary<string, int> counts =
                new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (Passenger passenger in passengers)
            {
                string profile = passenger == null || string.IsNullOrEmpty(passenger.ProfileName)
                    ? "(unassigned)"
                    : passenger.ProfileName;

                counts.TryGetValue(profile, out int current);
                counts[profile] = current + 1;
            }

            StringBuilder builder = new StringBuilder();

            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (builder.Length > 0)
                    builder.Append('|');

                builder.Append(pair.Key);
                builder.Append(':');
                builder.Append(pair.Value);
            }

            return builder.ToString();
        }

        /// <summary>
        /// True when the resolved plans contain a dedicated pool for the given
        /// channel. A shared plan does not count: its passengers are selected
        /// by the facility predicate rather than by channel identity.
        /// </summary>
        protected static bool HasConfiguredChannel(
            IList<ChannelResourcePlan> plans,
            string channel)
        {
            if (plans == null || string.IsNullOrWhiteSpace(channel))
                return false;

            string trimmed = channel.Trim();

            return plans.Any(plan =>
                plan != null &&
                !plan.IsSharedPool &&
                string.Equals(plan.Channel, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Builds the facility's passenger filter. When a channel list is wired
        /// the passenger must carry one of those channels; otherwise the
        /// facility's legacy predicate applies (so an unwired project behaves
        /// exactly as before).
        /// </summary>
        protected static Func<Passenger, bool> BuildChannelFilter(
            List<string> channels,
            Func<Passenger, bool> legacyPredicate)
        {
            if (channels == null || channels.Count == 0)
                return legacyPredicate;

            List<string> normalized = channels
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c.Trim())
                .ToList();

            if (normalized.Count == 0)
                return legacyPredicate;

            return p => p != null && normalized.Any(c =>
                string.Equals(c, p.Channel, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Picks the service-time provider, falling back to a constant provider
        /// around the legacy single-profile input. When a facility name is given,
        /// a facility-tagged <see cref="ServiceTimeMap"/> is scoped to it first,
        /// so the same key can hold different values at different facilities.
        /// Scoping problems (unknown tags, an emptied scope, foreign stage keys)
        /// are reported through <paramref name="warn"/>.
        /// </summary>
        protected static IServiceTimeProvider ResolveServiceTimeProvider(
            IServiceTimeProvider provider,
            ServiceProfile fallback,
            string facility = null,
            Action<string> warn = null)
        {
            IServiceTimeProvider picked;

            if (provider != null)
                picked = provider;
            else if (fallback != null)
                picked = new ConstantServiceProvider(fallback);
            else
                return null;

            ServiceTimeMap map = picked as ServiceTimeMap;

            if (map != null && !string.IsNullOrWhiteSpace(facility))
                return map.ScopedTo(facility, warn);

            return picked;
        }

        /// <summary>
        /// Single, shared solver cache key. Includes everything that can move the
        /// result: population size and demand tags, the realized arrival schedule
        /// and show-up fingerprints, service-time mapping, channel filter and
        /// channel mix, target, staffing, seed and optimize flag.
        /// </summary>
        protected static string BuildSolverCacheKey(
            List<Passenger> passengers,
            IServiceTimeProvider provider,
            List<string> channels,
            double targetWait,
            double staffingPercentage,
            int seed,
            bool optimize)
        {
            string providerKey = provider != null ? provider.CacheKey : "none";

            string channelFilter = channels != null && channels.Count > 0
                ? string.Join(",", channels
                    .Where(c => !string.IsNullOrEmpty(c))
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
                : "-";

            int count = passengers != null ? passengers.Count : 0;

            return $"{count}_{providerKey}_{channelFilter}_{GetChannelSignature(passengers)}_{GetProfileSignature(passengers)}_{GetDemandSignature(passengers)}_{GetShowUpSignature(passengers)}_{GetScheduleSignature(passengers)}_{targetWait}_{staffingPercentage}_{seed}_{optimize}";
        }

        /// <summary>
        /// Cache key for the channel-pool solver. Extends the shared key with the
        /// resolved pool plan (channel, physical count, target and search bounds)
        /// so any change to the sizing setup forces a re-optimization.
        /// </summary>
        protected static string BuildSolverCacheKey(
            List<Passenger> passengers,
            IServiceTimeProvider provider,
            List<string> channels,
            IList<ChannelResourcePlan> plans,
            double staffingPercentage,
            int seed,
            bool optimize)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append(BuildSolverCacheKey(
                passengers, provider, channels, 0.0, staffingPercentage, seed, optimize));

            builder.Append("|plans");

            if (plans != null)
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    ChannelResourcePlan plan = plans[i];

                    builder.Append(';');
                    builder.Append(plan.Channel ?? "*");
                    builder.Append('|');
                    builder.Append(plan.PoolKey ?? "*");
                    builder.Append(':');
                    builder.Append(plan.PhysicalResources);
                    builder.Append(':');
                    builder.Append(plan.TargetWait95.ToString("R"));
                    builder.Append(':');
                    builder.Append(plan.Range);
                }
            }

            return builder.ToString();
        }

        protected QueueResults SimulateQueue(
            List<Passenger> passengers,
            List<QueueResource> resources)
        {
            _queueSimulator.ResetPassengers(passengers);
            _queueSimulator.ResetResources(resources);

            _queueSimulator.RunQueue(passengers, resources);

            QueueResults results = _metricsCalculator.Calculate(passengers, resources);

            results.ProcessedPassengers = passengers;
            results.ResourcesUsed = resources.Count;

            return results;
        }

        /// <summary>
        /// Resets and replays the discrete-event simulation without computing any
        /// metrics. Used by the optimizer, which only needs the Wait95 of the
        /// current capacity and must not pay for group/interval/timeline work.
        /// </summary>
        protected void SimulateQueueOnly(
            List<Passenger> passengers,
            List<QueueResource> resources)
        {
            _queueSimulator.ResetPassengers(passengers);
            _queueSimulator.ResetResources(resources);

            _queueSimulator.RunQueue(passengers, resources, sortPassengers: false);
        }

        /// <summary>
        /// Builds a capacity-independent pool: filters and chronologically sorts
        /// the passengers that share one server pool, clones them so the
        /// simulation never mutates the upstream population, then assigns their
        /// service times once from the seed.
        ///
        /// Capacity changes never change service times, so hoisting this out of
        /// the optimizer loop keeps every candidate comparable and removes the
        /// per-candidate resampling cost. Only the served subset is cloned;
        /// passengers this facility does not route through pass straight on to
        /// the next component untouched.
        /// </summary>
        protected List<Passenger> BuildPool(
            IEnumerable<Passenger> passengers,
            Func<Passenger, bool> predicate,
            IServiceTimeProvider serviceTimeProvider,
            int seed,
            Dictionary<Passenger, Passenger> clones,
            out int designStepIndex)
        {
            List<Passenger> served = FilterPassengers(passengers, predicate);

            List<Passenger> pool = new List<Passenger>(served.Count);

            for (int i = 0; i < served.Count; i++)
            {
                Passenger source = served[i];

                if (source == null)
                {
                    pool.Add(null);
                    continue;
                }

                Passenger clone = source.Clone();

                PreparePoolPassenger(clone, source);

                if (clones != null)
                    clones[source] = clone;

                pool.Add(clone);
            }

            designStepIndex = -1;

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && pool[i].IsDesignStep)
                {
                    designStepIndex = pool[i].DemandStepIndex;
                    break;
                }
            }

            AssignServiceTimes(pool, serviceTimeProvider, new Random(seed));

            return pool;
        }

        /// <summary>
        /// Allows a facility to prepare a served passenger clone before it is
        /// simulated. The upstream passenger remains untouched, and the
        /// optimizer will reset the clone to its prepared QueueArrivalTime for
        /// each capacity trial.
        /// </summary>
        protected virtual void PreparePoolPassenger(
            Passenger passenger,
            Passenger source)
        {
        }

        /// <summary>
        /// Warns once per pool whose target wait could not be met inside the
        /// resource domain. The optimizer still returns the domain maximum, so
        /// without this the reported W95 can exceed the target silently.
        /// </summary>
        private static void WarnOnMissedTargets(
            List<ResourceSearchOutcome> chosen,
            Action<string> warn)
        {
            if (warn == null || chosen == null)
                return;

            foreach (ResourceSearchOutcome outcome in chosen)
            {
                if (outcome == null || outcome.Success)
                    continue;

                string pool = outcome.Plan != null && !string.IsNullOrWhiteSpace(outcome.Plan.PoolIdentity)
                    ? outcome.Plan.PoolIdentity
                    : "Shared";

                double target = outcome.Plan != null ? outcome.Plan.TargetWait95 : 0.0;
                int max = outcome.Plan != null && outcome.Plan.Range != null
                    ? outcome.Plan.Range.Normalized().Max
                    : outcome.OptimalPhysicalResources;

                warn($"Target Wait {target:0.##} min not met for pool '{pool}': " +
                     $"whole-run Wait95 is {outcome.Wait95:0.##} min at the maximum {max} resources. " +
                     $"Widen the resource domain or relax the target.");
            }
        }

        /// <summary>
        /// Simulates one candidate capacity for a pool and returns only the
        /// whole-run Wait95 the optimizer judges against (all non-warm-up
        /// passengers, matching the reported W95 outputs). No metrics objects
        /// are built.
        /// </summary>
        protected double EvaluateCapacity(
            List<Passenger> pool,
            int activeResources)
        {
            if (pool == null || pool.Count == 0)
                return 0.0;

            List<QueueResource> resources = QueueResource.Create(Math.Max(1, activeResources));

            SimulateQueueOnly(pool, resources);

            return _metricsCalculator.EvaluateWaitOnly(pool, -1);
        }

        /// <summary>
        /// Final full simulation of a pool at the chosen capacity, including the
        /// complete metrics object. Runs once per pool per solve.
        /// </summary>
        protected QueueResults FinalizePool(
            List<Passenger> pool,
            int activeResources)
        {
            List<QueueResource> resources = QueueResource.Create(Math.Max(1, activeResources));

            QueueResults results = SimulateQueue(pool, resources);

            results.Passengers = pool;

            return results;
        }

        protected void AssignServiceTimes(
            List<Passenger> passengers,
            ServiceProfile serviceProfile,
            Random random)
        {
            AssignServiceTimes(
                passengers,
                serviceProfile == null ? null : new ConstantServiceProvider(serviceProfile),
                random);
        }

        /// <summary>
        /// Assigns a service time to every passenger using the provider's
        /// per-passenger resolution. Passengers are grouped by resolved
        /// distribution in first-seen order, so each group is sampled from one
        /// lognormal and the draw order is stable across optimizer trials.
        /// </summary>
        protected void AssignServiceTimes(
            List<Passenger> passengers,
            IServiceTimeProvider serviceTimeProvider,
            Random random)
        {
            if (passengers == null || passengers.Count == 0)
                return;

            List<string> keys = new List<string>();
            List<ServiceProfile> profiles = new List<ServiceProfile>();
            List<List<Passenger>> groups = new List<List<Passenger>>();

            foreach (Passenger passenger in passengers)
            {
                ServiceProfile profile =
                    serviceTimeProvider != null ? serviceTimeProvider.Resolve(passenger) : null;

                if (profile == null)
                    profile = ZeroServiceProfile;

                string key =
                    profile.MeanServiceTime.ToString("R") + "," +
                    profile.StandardDeviation.ToString("R");

                int index = keys.IndexOf(key);

                if (index < 0)
                {
                    keys.Add(key);
                    profiles.Add(profile);
                    groups.Add(new List<Passenger>());
                    index = profiles.Count - 1;
                }

                groups[index].Add(passenger);
            }

            for (int g = 0; g < groups.Count; g++)
            {
                ServiceProfile profile = profiles[g];
                List<Passenger> group = groups[g];

                if (profile == null || profile.MeanServiceTime <= 0.0)
                {
                    foreach (Passenger passenger in group)
                        passenger.ServiceTime = 0.0;

                    continue;
                }

                List<double> serviceTimes = _serviceGenerator.Generate(
                        group.Count,
                        profile.MeanServiceTime,
                        profile.StandardDeviation,
                        random);

                for (int i = 0; i < group.Count; i++)
                {
                    group[i].ServiceTime = serviceTimes[i];
                }
            }
        }

        private static readonly ServiceProfile ZeroServiceProfile =
            new ServiceProfile { MeanServiceTime = 0.0, StandardDeviation = 0.0 };

        protected void AdvancePassengers(IEnumerable<Passenger> passengers)
        {
            foreach (Passenger passenger in passengers)
            {
                // FIX: Facilities should only update the clock to when service finished!
                // The WalkingComponent will handle adding travel times.
                passenger.ArrivalTime = passenger.EndServiceTime;
                passenger.QueueArrivalTime = passenger.ArrivalTime;

                passenger.WalkingTime = 0.0; // Reset it so it doesn't carry over accidentally
            }
        }

        protected QueueResults CreateEmptyResult(List<Passenger> passengers)
        {
            return new QueueResults
            {
                Passengers = passengers,
                ProcessedPassengers = new List<Passenger>(),
                Resources = new List<QueueResource>(),
                ResourcesUsed = 0,
                PeakQueueLength = 0,
                AverageWait = 0,
                MaxWait = 0,
                Wait95 = 0,
                SimulationDuration = 0,
                CounterUtilization = new List<double>()
            };
        }
    }
}
