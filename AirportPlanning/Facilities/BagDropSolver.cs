using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;

namespace AirportPlanning.Facilities
{
    public class BagDropSolver : FacilitySolverBase
    {
        private readonly QueueOptimizer _optimizer;

        public BagDropSolver() : base()
        {
            _optimizer = new QueueOptimizer();
        }

        public BagDropResults Solve(BagDropInputs input)
        {
            BagDropResults finalResult = new BagDropResults();
            QueueResults results;
            int originalPhysical = input.NumberOfResources;

            if (input.OptimizeResources)
            {
                OptimizationResult opt = _optimizer.Optimize(
                        input,
                        SolveWithoutOptimization,
                        (i, r) => i.NumberOfResources = r,
                        i => i.NumberOfResources,
                        input.TargetMaximumWait,
                        input.StaffingPercentage);

                results = opt.QueueResults;
                finalResult.PhysicalCounters = opt.OptimalPhysicalResources;
                finalResult.ActiveCounters = opt.OptimalActiveResources;
            }
            else
            {
                int activeN = (int)Math.Max(1, Math.Round(originalPhysical * (input.StaffingPercentage / 100.0)));
                input.NumberOfResources = activeN;
                results = SolveWithoutOptimization(input);

                finalResult.PhysicalCounters = originalPhysical;
                finalResult.ActiveCounters = activeN;
            }

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
            return ExecuteFacility(
                input.Passengers,
                p => p.RequiresBagDrop,
                input.NumberOfResources,
                input.ServiceProfile,
                new Random(input.Seed));
        }
    }
}
