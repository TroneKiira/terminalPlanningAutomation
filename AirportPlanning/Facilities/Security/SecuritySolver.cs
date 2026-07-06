using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities.Security
{
    public class SecuritySolver : FacilitySolverBase
    {
        private readonly QueueOptimizer _optimizer;

        public SecuritySolver()
            : base()
        {
            _optimizer = new QueueOptimizer();
        }

        public QueueResults Solve(SecurityInputs input)
        {
            QueueResults results;

            if (input.OptimizeResources)
            {
                OptimizationResult optimization =
                    _optimizer.Optimize(
                        input,
                        SolveWithoutOptimization,
                        (i, r) => i.NumberOfResources = r,
                        i => i.NumberOfResources,
                        input.TargetMaximumWait,
                        input.StaffingPercentage); // NEW: Pass StaffingPercentage

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
            SecurityInputs input)
        {
            return ExecuteFacility(
                input.Passengers,
                p => true,
                input.NumberOfResources,
                input.ServiceProfile,
                new Random(input.Seed));
        }
    }
}
