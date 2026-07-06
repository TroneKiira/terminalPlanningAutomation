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

        public ImmigrationSolver()
            : base()
        {
            _optimizer = new QueueOptimizer();
        }

        public QueueResults Solve(ImmigrationInputs input)
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
                        input.StaffingPercentage);

                results = optimization.QueueResults;
            }
            else
            {
                int activeN = (int)Math.Max(1, Math.Round(input.NumberOfResources * (input.StaffingPercentage / 100.0)));
                input.NumberOfResources = activeN;

                results = SolveWithoutOptimization(input);
            }

            // NEW: Save the historical record for Immigration
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
            return results;
        }

        private QueueResults SolveWithoutOptimization(
            ImmigrationInputs input)
        {
            return ExecuteFacility(
                input.Passengers,
                p => p.PassengerType == PassengerType.International,
                input.NumberOfResources,
                input.ServiceProfile,
                new Random(input.Seed));
        }
    }
}
