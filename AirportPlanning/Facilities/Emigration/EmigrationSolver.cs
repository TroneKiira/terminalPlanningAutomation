using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facilities.Emigration
{
    /// <summary>
    /// Outbound passport control for international departing passengers.
    /// E-Gate and manual counter positions are modelled as separate pools when
    /// matching Pool Plans are supplied.
    /// </summary>
    public class EmigrationSolver : FacilitySolverBase
    {
        /// <summary>Canonical Pool Plan identity for automated e-gate processors.</summary>
        public const string EGatePool = "EGate";

        /// <summary>Canonical Pool Plan identity for manual emigration counters.</summary>
        public const string CounterPool = "Counter";

        private string _lastCacheKey = "";
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public EmigrationSolver() : base()
        {
        }

        public EmigrationResults Solve(EmigrationInputs input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            Warnings.Clear();

            EmigrationResults finalResult = new EmigrationResults();

            List<Passenger> workingPassengers = (input.Passengers ?? new List<Passenger>())
                .Where(p => p != null)
                .Select(p => p.Clone())
                .ToList();

            List<ChannelResourcePlan> plans = ResolvePlans(input);

            Dictionary<Passenger, string> originalChannels = new Dictionary<Passenger, string>();
            AssignProcessorRoutes(
                workingPassengers,
                input.EGateSharePercentage,
                input.Seed,
                plans,
                input.Channels,
                originalChannels);

            IServiceTimeProvider serviceTimeProvider =
                ResolveServiceTimeProvider(input.ServiceTimeProvider, input.ServiceProfile);

            if (serviceTimeProvider == null)
            {
                Warnings.Add(
                    "Emigration has no service-time source: wire either Service Profile or " +
                    "Service Profiles (Service Time Map). Without one every service time is 0 " +
                    "and the result is meaningless.");
            }

            string currentCacheKey = BuildSolverCacheKey(
                workingPassengers,
                serviceTimeProvider,
                input.Channels,
                plans,
                input.StaffingPercentage,
                input.Seed,
                input.OptimizeResources);

            bool cacheHit = currentCacheKey == _lastCacheKey &&
                            _cachedOutcomes != null &&
                            _cachedBaselinePhysical > 0;

            List<ResourceSearchOutcome> outcomes;

            Func<Passenger, bool> emigrationPassenger = p =>
                p != null &&
                p.PassengerType == PassengerType.International &&
                IsDeparturePassenger(p);

            FacilitySolveResult solve = SolvePools(
                workingPassengers,
                plans,
                input.Channels,
                emigrationPassenger,
                serviceTimeProvider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add,
                emigrationPassenger);

            if (!cacheHit && input.OptimizeResources && outcomes != null)
            {
                _cachedOutcomes = outcomes;
                _cachedBaselinePhysical = solve.TotalPhysical;
                _lastCacheKey = currentCacheKey;
            }

            foreach (KeyValuePair<Passenger, Passenger> pair in solve.Clones)
            {
                if (!originalChannels.TryGetValue(pair.Key, out string originalChannel))
                    continue;

                pair.Key.Channel = originalChannel;
                pair.Value.Channel = originalChannel;
            }

            QueueResults results = solve.Results;

            results.Passengers = BuildOutputPopulation(workingPassengers, solve.Clones);

            finalResult.QueueResults = results;
            finalResult.PhysicalPositions = solve.TotalPhysical;
            finalResult.ActivePositions = solve.TotalActive;
            finalResult.PhysicalPerChannel = solve.PhysicalPerChannel;
            finalResult.ActivePerChannel = solve.ActivePerChannel;
            finalResult.Wait95PerChannel = solve.Wait95PerChannel;
            finalResult.PoolNames = solve.PoolNames;
            finalResult.NominalPerChannel = plans
                .Select(p => p.PhysicalResources)
                .ToList();
            finalResult.PoolResults = solve.PoolResults;
            finalResult.SearchOutcomes = solve.SearchOutcomes;

            PoolPlanResolver.WarnOnEmptyPools(plans, solve.PoolResults, Warnings.Add);
            PoolPlanResolver.WarnOnUncoveredChannels(plans, input.Channels, Warnings.Add);

            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.Emigration);
            AdvancePassengers(solve.Processed);

            return finalResult;
        }

        /// <summary>
        /// Splits international departures between the e-gate and counter pools
        /// when those processor pools are configured. The original passenger
        /// channel is saved so check-in routing survives emigration.
        /// </summary>
        private void AssignProcessorRoutes(
            List<Passenger> passengers,
            double sharePercentage,
            int seed,
            IList<ChannelResourcePlan> plans,
            List<string> channels,
            Dictionary<Passenger, string> originalChannels)
        {
            bool configured = UsesProcessorRoutes(plans) ||
                (channels != null && channels.Any(IsProcessorRoute));

            if (!configured)
                return;

            double share = sharePercentage;

            if (double.IsNaN(share) || double.IsInfinity(share))
                share = 40.0;

            double normalizedShare = Math.Max(0.0, Math.Min(100.0, share));

            if (normalizedShare != share)
            {
                Warnings.Add(
                    "E-Gate Share must be between 0 and 100%; it was clamped to the valid range.");
            }

            List<Passenger> eligible = passengers
                .Where(p => p != null &&
                            p.PassengerType == PassengerType.International &&
                            IsDeparturePassenger(p))
                .ToList();

            if (eligible.Count == 0)
                return;

            foreach (Passenger passenger in eligible)
                originalChannels[passenger] = passenger.Channel;

            int eGateCount = (int)Math.Round(
                eligible.Count * normalizedShare / 100.0,
                MidpointRounding.AwayFromZero);

            Random random = new Random(seed);

            for (int i = eligible.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                Passenger swap = eligible[i];
                eligible[i] = eligible[j];
                eligible[j] = swap;
            }

            for (int i = 0; i < eligible.Count; i++)
            {
                string route = i < eGateCount ? EGatePool : CounterPool;
                eligible[i].EmigrationRoute = route;
                eligible[i].Channel = route;
            }
        }

        private static bool UsesProcessorRoutes(IList<ChannelResourcePlan> plans)
        {
            if (plans == null)
                return false;

            return plans.Any(p => p != null && !p.IsSharedPool && IsProcessorRoute(p.Channel));
        }

        private static bool IsProcessorRoute(string channel)
        {
            return string.Equals(channel, EGatePool, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(channel, CounterPool, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Resolves channel resource plans from keyed PoolPlans when supplied,
        /// otherwise falls back to the legacy PhysicalResources/TargetWait95
        /// lists with a shared ResourceDomain.
        /// </summary>
        private List<ChannelResourcePlan> ResolvePlans(EmigrationInputs input)
        {
            ResourceSearchRange fallbackDomain =
                input.ResourceDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources);

            if (input.PoolPlans != null && input.PoolPlans.Count > 0)
            {
                return PoolPlanResolver.Resolve(
                    input.PoolPlans, fallbackDomain, Warnings.Add)
                    ?? ResolveLegacyPlans(input, fallbackDomain);
            }

            return ResolveLegacyPlans(input, fallbackDomain);
        }

        private List<ChannelResourcePlan> ResolveLegacyPlans(
            EmigrationInputs input,
            ResourceSearchRange fallbackDomain)
        {
            return ChannelResourcePlanResolver.Resolve(
                input.Channels,
                input.PhysicalResources,
                input.TargetWait95,
                fallbackDomain,
                Warnings.Add);
        }
    }
}
