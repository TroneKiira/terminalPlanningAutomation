using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using Rhino;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facilities.CheckIn
{
    public class CheckInSolver : FacilitySolverBase
    {
        private readonly QueueOptimizer _optimizer;

        // 🧠 CACHE VARIABLES
        private string _lastCacheKey = "";
        private int _cachedBaselinePhysical = -1;
        private int _cachedBaselineActive = -1;

        public CheckInSolver() : base()
        {
            _optimizer = new QueueOptimizer();
        }

        public CheckInResults Solve(CheckInInputs input)
        {
            CheckInResults finalResult = new CheckInResults();
            QueueResults results;

            // 1. Generate the unique fingerprint for the current inputs
            string currentCacheKey = $"{input.Passengers.Count}_{input.ServiceProfile?.MeanServiceTime}_{input.TargetMaximumWait}_{input.StaffingPercentage}_{input.Seed}";

            // 2. Only run the heavy optimizer if the inputs changed, or if we don't have a cache yet!
            if (currentCacheKey != _lastCacheKey || _cachedBaselinePhysical <= 0)
            {
                OptimizationResult opt = _optimizer.Optimize(
                        input,
                        SolveWithoutOptimization,
                        (i, c) => i.NumberOfResources = c,
                        i => i.NumberOfResources,
                        input.TargetMaximumWait,
                        input.StaffingPercentage);

                // Save the results to the cache
                _cachedBaselinePhysical = opt.OptimalPhysicalResources;
                _cachedBaselineActive = opt.OptimalActiveResources;
                _lastCacheKey = currentCacheKey;
            }

            int originalPhysical;
            int activeN;

            // 3. Determine which numbers to use for the final run
            if (input.OptimizeResources)
            {
                originalPhysical = _cachedBaselinePhysical;
                activeN = _cachedBaselineActive;
            }
            else
            {
                originalPhysical = input.NumberOfResources;
                activeN = (int)Math.Max(1, Math.Round(originalPhysical * (input.StaffingPercentage / 100.0)));
            }

            // 4. Do ONE single simulation run with the chosen numbers
            input.NumberOfResources = activeN;
            results = SolveWithoutOptimization(input);

            // 5. Package the results and embed the secret baseline
            finalResult.PhysicalCounters = originalPhysical;
            finalResult.ActiveCounters = activeN;

            // 🚨 THIS PASSES THE BASELINE TO THE REPORTER
            results.BaselineProcessorCount = _cachedBaselinePhysical;

            foreach (Passenger p in results.ProcessedPassengers)
            {
                p.History[FacilityType.CheckIn] = new FacilityRecord
                {
                    ArrivalTime = p.ArrivalTime,
                    StartServiceTime = p.StartServiceTime,
                    EndServiceTime = p.EndServiceTime,
                    WaitTime = p.WaitTime,
                    AssignedResource = p.AssignedCounter
                };
            }

            AdvancePassengers(results.ProcessedPassengers);

            finalResult.QueueResults = results;
            return finalResult;
        }

        private QueueResults SolveWithoutOptimization(CheckInInputs input)
        {
            return ExecuteFacility(
                input.Passengers,
                p => p.RequiresCheckIn,
                input.NumberOfResources,
                input.ServiceProfile,
                new Random(input.Seed));
        }
    }
}