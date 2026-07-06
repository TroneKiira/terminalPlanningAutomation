using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities.Immigration
{
    public class ImmigrationSolver : FacilitySolverBase
    {
        private readonly QueueOptimizer _optimizer;

        public ImmigrationSolver() : base()
        {
            _optimizer = new QueueOptimizer();
        }

        public ImmigrationResults Solve(ImmigrationInputs input)
        {
            ImmigrationResults finalResult = new ImmigrationResults();
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
                p.History[FacilityType.Immigration] = new FacilityRecord
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

        private QueueResults SolveWithoutOptimization(ImmigrationInputs input)
        {
            return ExecuteFacility(
                input.Passengers, p => p.PassengerType == PassengerType.International,
                input.NumberOfResources, input.ServiceProfile, new Random(input.Seed));
        }
    }
}
