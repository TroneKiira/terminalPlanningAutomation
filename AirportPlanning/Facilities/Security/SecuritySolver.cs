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

        public SecuritySolver() : base()
        {
            _optimizer = new QueueOptimizer();
        }

        public SecurityResults Solve(SecurityInputs input)
        {
            SecurityResults finalResult = new SecurityResults();
            QueueResults results;
            int originalPhysical = input.NumberOfResources;

            if (input.OptimizeResources)
            {
                OptimizationResult opt = _optimizer.Optimize(
                        input, SolveWithoutOptimization, (i, r) => i.NumberOfResources = r,
                        i => i.NumberOfResources, input.TargetMaximumWait, input.StaffingPercentage);

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
            return ExecuteFacility(
                input.Passengers, p => true, input.NumberOfResources, input.ServiceProfile, new Random(input.Seed));
        }
    }
}
