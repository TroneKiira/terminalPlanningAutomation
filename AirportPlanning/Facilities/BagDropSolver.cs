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

        public BagDropSolver()
            : base()
        {
            _optimizer = new QueueOptimizer();
        }

        public QueueResults Solve(BagDropInputs input)
        {
            QueueResults results;

            if (input.OptimizeResources)
            {
                OptimizationResult optimization =
                    _optimizer.Optimize(
                        input,
                        SolveWithoutOptimization,
                        (i, r) => i.NumberOfResources = r, // Sets Active Staffed count temporarily
                        i => i.NumberOfResources,          // Reads original Physical count
                        input.TargetMaximumWait,
                        input.StaffingPercentage);         // NEW: Passing the required argument

                results= optimization.QueueResults;
            }
            else
            {
                // Single run: Apply staffing percentage to physical resources immediately
                int activeN = (int)Math.Max(1, Math.Round(input.NumberOfResources * (input.StaffingPercentage / 100.0)));
                input.NumberOfResources = activeN;

                results = SolveWithoutOptimization(input);
            }

            // 3. Save the history snapshot ONCE, no matter which block ran above
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

            // 4. Advance the passengers and return
            AdvancePassengers(results.ProcessedPassengers);
            return results;
        }

        private QueueResults SolveWithoutOptimization(
            BagDropInputs input)
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
