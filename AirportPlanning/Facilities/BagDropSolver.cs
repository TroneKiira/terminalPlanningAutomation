using System;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;

namespace AirportPlanning.Facilities
{
    public class BagDropSolver : FacilitySolverBase
    {
        private readonly QueueOptimizer _optimizer;
        private string _lastCacheKey = "";
        private int _cachedBaselinePhysical = -1;
        private int _cachedBaselineActive = -1;

        public BagDropSolver() : base() { _optimizer = new QueueOptimizer(); }

        public BagDropResults Solve(BagDropInputs input)
        {
            BagDropResults finalResult = new BagDropResults();
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
                p.History[FacilityType.BagDrop] = new FacilityRecord
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

        private QueueResults SolveWithoutOptimization(BagDropInputs input)
        {
            return ExecuteFacility(input.Passengers, p => p.RequiresBagDrop, input.NumberOfResources, input.ServiceProfile, new Random(input.Seed));
        }
    }
}