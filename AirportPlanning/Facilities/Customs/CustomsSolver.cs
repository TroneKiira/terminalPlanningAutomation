using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facilities.Customs
{
    /// <summary>
    /// Arrival-side customs hand-baggage screening. Serves passengers entering
    /// the country — international arrivals and International→Domestic
    /// transfers — after immigration and before baggage reclaim. Domestic
    /// arrivals never reach it, and International→International transfers stay
    /// airside, so both pass through untouched.
    ///
    /// One facility, one passenger class: there is no transfer channel and no
    /// pool segregation. Built on the same queue-and-pool machinery as the
    /// other facilities, so the shared pool is the default and pool plans are
    /// honoured when supplied.
    /// </summary>
    public class CustomsSolver : FacilitySolverBase
    {
        private string _lastCacheKey = "";
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public CustomsSolver() : base() { }

        /// <summary>
        /// True when the passenger must clear hand-baggage customs at this
        /// terminal. See <see cref="CustomsEligibility"/>: international
        /// arrivals entering the country qualify; domestic arrivals and
        /// International→International transfers do not.
        /// </summary>
        public static bool RequiresCustoms(Passenger passenger)
        {
            return CustomsEligibility.RequiresHandBaggageCustoms(passenger);
        }

        public CustomsResults Solve(CustomsInputs input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            Warnings.Clear();

            CustomsResults finalResult = new CustomsResults();

            List<Passenger> workingPassengers = (input.Passengers ?? new List<Passenger>())
                .Where(p => p != null)
                .Select(p => p.Clone())
                .ToList();

            List<ChannelResourcePlan> plans = ResolvePlans(input);

            IServiceTimeProvider serviceTimeProvider =
                ResolveServiceTimeProvider(input.ServiceTimeProvider, input.ServiceProfile, FacilityScopes.Customs, Warnings.Add);

            if (serviceTimeProvider == null)
            {
                Warnings.Add(
                    "Customs hand baggage has no service-time source: wire either Service Profile or " +
                    "Service Profiles (Service Time Map). Without one every service time is 0 " +
                    "and the result is meaningless.");
            }

            Func<Passenger, bool> customsPassenger = RequiresCustoms;

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

            FacilitySolveResult solve = SolvePools(
                workingPassengers,
                plans,
                input.Channels,
                customsPassenger,
                serviceTimeProvider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add,
                customsPassenger);

            if (!cacheHit && input.OptimizeResources && outcomes != null)
            {
                _cachedOutcomes = outcomes;
                _cachedBaselinePhysical = solve.TotalPhysical;
                _lastCacheKey = currentCacheKey;
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

            PoolPlanResolver.WarnOnEmptyPools(plans, solve.PoolResults, workingPassengers, Warnings.Add);
            PoolPlanResolver.WarnOnUncoveredChannels(plans, input.Channels, Warnings.Add);

            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.Customs);
            AdvancePassengers(solve.Processed);

            return finalResult;
        }

        /// <summary>
        /// Resolves channel resource plans from keyed PoolPlans when supplied,
        /// otherwise falls back to the legacy PhysicalResources/TargetWait95
        /// lists with a shared ResourceDomain.
        /// </summary>
        private List<ChannelResourcePlan> ResolvePlans(CustomsInputs input)
        {
            ResourceSearchRange fallbackDomain =
                input.ResourceDomain ?? new ResourceSearchRange(
                    1, ChannelResourcePlanResolver.DefaultMaxResources);

            if (input.PoolPlans != null && input.PoolPlans.Count > 0)
            {
                return PoolPlanResolver.Resolve(
                    input.PoolPlans, fallbackDomain, Warnings.Add, FacilityScopes.Customs)
                    ?? ResolveLegacyPlans(input, fallbackDomain);
            }

            return ResolveLegacyPlans(input, fallbackDomain);
        }

        private List<ChannelResourcePlan> ResolveLegacyPlans(
            CustomsInputs input,
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
