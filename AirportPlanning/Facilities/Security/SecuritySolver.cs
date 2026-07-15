using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facilities.Security
{
    public class SecuritySolver : FacilitySolverBase
    {
        private readonly QueueOptimizer _optimizer;
        private string _lastCacheKey = "";
        private int _cachedBaselinePhysical = -1;
        private int _cachedBaselineActive = -1;

        public SecuritySolver() : base() { _optimizer = new QueueOptimizer(); }

        public SecurityResults Solve(SecurityInputs input)
        {
            SecurityResults finalResult = new SecurityResults();
            QueueResults results;

            string currentCacheKey = $"{input.Passengers.Count}_{input.ServiceProfile?.MeanServiceTime}_{input.TargetMaximumWait}_{input.StaffingPercentage}_{input.Seed}";

            if (currentCacheKey != _lastCacheKey || _cachedBaselinePhysical <= 0)
            {
                OptimizationResult opt = _optimizer.Optimize(
                        input, SolveWithoutOptimization, (i, r) => i.NumberOfResources = r,
                        i => i.NumberOfResources, input.TargetMaximumWait, input.StaffingPercentage);

                _cachedBaselinePhysical = opt.OptimalPhysicalResources;
                _cachedBaselineActive = opt.OptimalActiveResources;
                _lastCacheKey = currentCacheKey;
            }

            int originalPhysical;
            int activeN;

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

            input.NumberOfResources = activeN;
            results = SolveWithoutOptimization(input);

            finalResult.PhysicalCounters = originalPhysical;
            finalResult.ActiveCounters = activeN;

            results.BaselineProcessorCount = _cachedBaselinePhysical;

            // 🚨 THE FIX: Pass the physical count down the wire!
            results.PhysicalProcessorCount = originalPhysical;

            foreach (Passenger p in results.ProcessedPassengers)
            {
                p.History[FacilityType.Security] = new FacilityRecord
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

        private QueueResults SolveWithoutOptimization(SecurityInputs input)
        {
            return ExecuteFacility(input.Passengers, p => true, input.NumberOfResources, input.ServiceProfile, new Random(input.Seed));
        }
    }
}