using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Inputs;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facilities
{
    public class BagDropSolver : FacilitySolverBase
    {
        private string _lastCacheKey = "";
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public BagDropSolver() : base() { }

        public BagDropResults Solve(BagDropInputs input)
        {
            Warnings.Clear();

            BagDropResults finalResult = new BagDropResults();

            IServiceTimeProvider serviceTimeProvider = ResolveServiceTimeProvider(input.ServiceTimeProvider, input.ServiceProfile, FacilityScopes.BagDrop, Warnings.Add);

            List<ChannelResourcePlan> plans = ResolvePlans(input);

            int alreadyDropped = input.Passengers == null
                ? 0
                : input.Passengers.Count(p =>
                    p != null && p.RequiresSeparateBagDrop && HasBagDropHistory(p));

            if (alreadyDropped > 0)
            {
                Warnings.Add(
                    $"{alreadyDropped} passenger(s) already carry a Bag Drop record from an " +
                    "earlier Bag Drop stage and are not served again.");
            }

            if (input.Channels != null &&
                input.Channels.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                Warnings.Add(
                    "Bag Drop ignores its Channels input: passengers are selected by their " +
                    "bag-drop requirement. Counter passengers have their bag accepted at " +
                    "check-in; Web and Kiosk passengers with a checked bag come here.");
            }

            if (serviceTimeProvider == null)
            {
                Warnings.Add(
                    "Bag Drop has no service-time source: wire either Service Profile or " +
                    "Service Profiles (Service Time Map). Without one every service time is 0 " +
                    "and the result is meaningless.");
            }

            string currentCacheKey = BuildSolverCacheKey(
                input.Passengers,
                serviceTimeProvider,
                new List<string>(),
                plans,
                input.StaffingPercentage,
                input.Seed,
                input.OptimizeResources);

            bool cacheHit = currentCacheKey == _lastCacheKey &&
                            _cachedOutcomes != null &&
                            _cachedBaselinePhysical > 0;

            List<ResourceSearchOutcome> outcomes;

            // Keyed pool plans are segment queues: 'BagDrop|DHPType|ProfileName'
            // pools one profile slice of the bag-drop population. Invalid keys
            // are rejected, and passengers whose segment no plan covers fall
            // back to a shared pool so nobody is silently dropped. The
            // separate-bag-drop requirement is part of the required predicate
            // so it holds for keyed pools too: a Counter passenger's bag is
            // accepted at check-in and can never enter a Bag Drop queue.
            Func<Passenger, bool> requiredPredicate =
                p => p.RequiresSeparateBagDrop &&
                     IsDeparturePassenger(p) &&
                     !HasBagDropHistory(p) &&
                     TransferServiceUsageCalculator.UsesService(p, TransferService.BagDrop);

            plans = PreparePlans(plans, input, requiredPredicate, out Func<Passenger, bool> sharedPoolFilter);

            FacilitySolveResult solve = SolvePools(
                input.Passengers,
                plans,
                new List<string>(),
                p => p.RequiresSeparateBagDrop,
                serviceTimeProvider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add,
                requiredPredicate,
                matchPrimaryChannel: false,
                sharedPoolFilter: sharedPoolFilter);

            if (!cacheHit && input.OptimizeResources && outcomes != null)
            {
                _cachedOutcomes = outcomes;
                _cachedBaselinePhysical = solve.TotalPhysical;
                _lastCacheKey = currentCacheKey;
            }

            QueueResults results = solve.Results;

            results.Passengers = BuildOutputPopulation(input.Passengers, solve.Clones);

            finalResult.QueueResults = results;
            finalResult.PhysicalCounters = solve.TotalPhysical;
            finalResult.ActiveCounters = solve.TotalActive;
            finalResult.PhysicalPerChannel = solve.PhysicalPerChannel;
            finalResult.ActivePerChannel = solve.ActivePerChannel;
            finalResult.Wait95PerChannel = solve.Wait95PerChannel;
            finalResult.PoolNames = solve.PoolNames;
            finalResult.NominalPerChannel = plans
                .Select(p => p.PhysicalResources)
                .ToList();
            finalResult.PoolResults = solve.PoolResults;
            finalResult.SearchOutcomes = solve.SearchOutcomes;

            PoolPlanResolver.WarnOnEmptyPools(plans, solve.PoolResults, input.Passengers, Warnings.Add);

            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.BagDrop);
            AdvancePassengers(solve.Processed);

            return finalResult;
        }

        private static bool HasBagDropHistory(Passenger passenger)
        {
            return passenger?.History != null &&
                   passenger.History.ContainsKey(FacilityType.BagDrop);
        }

        /// <summary>
        /// Validates and completes the keyed Bag Drop pool plans.
        ///
        /// Keyed bag-drop pools are SEGMENT queues: the identity must be
        /// 'BagDrop' or 'BagDrop|DHPType|ProfileName'. The BagDrop prefix is a
        /// segment label, never matched against a passenger's primary channel,
        /// so a Web or Kiosk passenger with a checked bag is served by the
        /// pool of their own profile slice. Plans naming another channel
        /// (for example a legacy 'Web|...' key) are channel-blind traps: they
        /// would swallow every profile slice regardless of the primary
        /// channel, so they are rejected with a warning.
        ///
        /// A facility-wide 'BagDrop' plan (no pool key) covers all demand; any
        /// segment plans supplied alongside it are redundant and ignored.
        /// Segment plans without a facility-wide plan keep a shared fallback
        /// pool for uncovered segments so no eligible passenger is dropped.
        /// </summary>
        private List<ChannelResourcePlan> PreparePlans(
            List<ChannelResourcePlan> plans,
            BagDropInputs input,
            Func<Passenger, bool> requiredPredicate,
            out Func<Passenger, bool> sharedPoolFilter)
        {
            sharedPoolFilter = null;

            if (plans == null || plans.Count == 0 || plans.All(p => p == null || p.IsSharedPool))
                return plans;

            List<ChannelResourcePlan> keyed = plans
                .Where(p => p != null && !p.IsSharedPool)
                .ToList();

            foreach (ChannelResourcePlan plan in keyed)
            {
                if (DefaultChannelAssigner.IsBagDropChannel(plan.Channel))
                    continue;

                Warnings.Add(
                    $"Bag Drop pool plan '{plan.PoolIdentity}' names the channel '{plan.Channel}'. " +
                    "Bag Drop pools must be keyed 'BagDrop' or 'BagDrop|DHPType|ProfileName'; the key " +
                    "selects a segment of the bag-drop population and is never matched against a " +
                    "passenger's primary check-in channel. The plan was ignored.");
            }

            List<ChannelResourcePlan> valid = keyed
                .Where(p => DefaultChannelAssigner.IsBagDropChannel(p.Channel))
                .ToList();

            if (valid.Count == 0)
            {
                // Nothing valid survived: fall back to the shared legacy pool
                // so the facility still serves its whole population.
                return new List<ChannelResourcePlan> { BuildFallbackPlan(input) };
            }

            // A facility-wide plan covers every eligible passenger; segment
            // plans alongside it would overlap it and serve records twice.
            if (valid.Any(p => string.IsNullOrWhiteSpace(p.PoolKey)))
            {
                foreach (ChannelResourcePlan plan in valid.Where(p => !string.IsNullOrWhiteSpace(p.PoolKey)))
                {
                    Warnings.Add(
                        $"Bag Drop pool plan '{plan.PoolIdentity}' is ignored because the facility-wide " +
                        "'BagDrop' plan already serves every bag-drop passenger.");
                }

                valid = valid.Where(p => string.IsNullOrWhiteSpace(p.PoolKey)).ToList();

                return sharedPlanFirst(plans, valid);
            }

            // Segment plans only: check the population for uncovered slices
            // and add one shared fallback pool when they exist.
            if (input.Passengers != null)
            {
                List<Passenger> uncovered = input.Passengers
                    .Where(p => p != null && requiredPredicate(p))
                    .Where(p => !MatchesAnyKeyedSegment(p, valid))
                    .ToList();

                if (uncovered.Count > 0)
                {
                    Warnings.Add(
                        $"{uncovered.Count} bag-drop passenger(s) match no keyed Bag Drop pool plan " +
                        $"({DescribeSegments(uncovered)}); a shared fallback pool serves them.");

                    sharedPoolFilter = p =>
                        p != null && !MatchesAnyKeyedSegment(p, valid);

                    List<ChannelResourcePlan> completed = new List<ChannelResourcePlan>(plans);
                    completed.Add(BuildFallbackPlan(input));
                    return completed;
                }
            }

            return plans;
        }

        /// <summary>
        /// Puts the surviving facility-wide plan first and drops every other
        /// keyed plan, so overlapping segment plans cannot double-serve.
        /// </summary>
        private static List<ChannelResourcePlan> sharedPlanFirst(
            List<ChannelResourcePlan> plans,
            List<ChannelResourcePlan> facilityWide)
        {
            List<ChannelResourcePlan> result = plans
                .Where(p => p != null && p.IsSharedPool)
                .ToList();

            result.AddRange(facilityWide);
            return result;
        }

        /// <summary>
        /// The fallback pool inherits the legacy Counters/Target Wait inputs,
        /// so a wired counters value still sizes the passengers no keyed plan
        /// covers.
        /// </summary>
        private ChannelResourcePlan BuildFallbackPlan(BagDropInputs input)
        {
            int physical = input.PhysicalResources != null && input.PhysicalResources.Count > 0
                ? Math.Max(1, input.PhysicalResources[0])
                : ChannelResourcePlanResolver.DefaultPhysicalResources;

            double target = input.TargetWait95 != null && input.TargetWait95.Count > 0
                ? input.TargetWait95[0]
                : ChannelResourcePlanResolver.DefaultTargetWait95;

            return new ChannelResourcePlan
            {
                Channel = null,
                PoolKey = null,
                PhysicalResources = physical,
                TargetWait95 = target > 0.0 ? target : ChannelResourcePlanResolver.DefaultTargetWait95,
                Range = input.ResourceDomain != null
                    ? input.ResourceDomain.Normalized()
                    : new ResourceSearchRange(1, ChannelResourcePlanResolver.DefaultMaxResources)
            };
        }

        /// <summary>Summarizes uncovered passengers as "DHPType|Profile count" rows.</summary>
        private static string DescribeSegments(List<Passenger> passengers)
        {
            return string.Join(", ", passengers
                .GroupBy(p => $"{p.DHPType}|{(string.IsNullOrEmpty(p.ProfileName) ? "(unassigned)" : p.ProfileName)}",
                    StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} {g.Count()}"));
        }

        /// <summary>
        /// Resolves channel resource plans from keyed PoolPlans when supplied,
        /// otherwise falls back to the legacy PhysicalResources/TargetWait95
        /// lists with a shared ResourceDomain.
        /// </summary>
        private List<ChannelResourcePlan> ResolvePlans(BagDropInputs input)
        {
            ResourceSearchRange fallbackDomain =
                input.ResourceDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources);

            if (input.PoolPlans != null && input.PoolPlans.Count > 0)
            {
                return PoolPlanResolver.Resolve(
                    input.PoolPlans, fallbackDomain, Warnings.Add, FacilityScopes.BagDrop)
                    ?? ResolveLegacyPlans(input, fallbackDomain);
            }

            return ResolveLegacyPlans(input, fallbackDomain);
        }

        private List<ChannelResourcePlan> ResolveLegacyPlans(
            BagDropInputs input,
            ResourceSearchRange fallbackDomain)
        {
            // Bag Drop has one shared queue by default. It is not a routed
            // primary channel, so a Channels list cannot define its pools.
            return ChannelResourcePlanResolver.Resolve(
                new List<string>(),
                input.PhysicalResources,
                input.TargetWait95,
                fallbackDomain,
                Warnings.Add);
        }
    }
}
