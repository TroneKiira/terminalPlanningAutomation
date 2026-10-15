using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.Immigration
{
    public class ImmigrationSolver : FacilitySolverBase
    {
        // CACHE VARIABLES
        private string _lastCacheKey = "";
        private List<ResourceSearchOutcome> _cachedOutcomes;
        private int _cachedBaselinePhysical = -1;

        public ImmigrationSolver() : base()
        {
        }

        public ImmigrationResults Solve(ImmigrationInputs input)
        {
            ImmigrationResults finalResult = new ImmigrationResults();

            IServiceTimeProvider serviceTimeProvider =
                ResolveServiceTimeProvider(input.ServiceTimeProvider, input.ServiceProfile);

            List<ChannelResourcePlan> plans = ChannelResourcePlanResolver.Resolve(
                input.Channels,
                input.PhysicalResources,
                input.TargetWait95,
                input.ResourceDomain,
                Warnings.Add);

            if (serviceTimeProvider == null)
            {
                Warnings.Add(
                    "Immigration has no service-time source: wire either Service Profile or " +
                    "Service Profiles (Service Time Map). Without one every service time is 0 " +
                    "and the result is meaningless.");
            }

            string currentCacheKey = BuildSolverCacheKey(
                input.Passengers,
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
                input.Passengers,
                plans,
                input.Channels,
                p => p.PassengerType == PassengerType.International,
                serviceTimeProvider,
                input.Seed,
                input.OptimizeResources,
                input.StaffingPercentage,
                cacheHit ? _cachedOutcomes : null,
                out outcomes,
                Warnings.Add);

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
            finalResult.PoolResults = solve.PoolResults;
            finalResult.SearchOutcomes = solve.SearchOutcomes;

            results.BaselineProcessorCount = _cachedBaselinePhysical;
            results.PhysicalProcessorCount = solve.TotalPhysical;

            WriteFacilityHistory(solve.Processed, FacilityType.Immigration);
            AdvancePassengers(solve.Processed);

            return finalResult;
        }
    }
}
