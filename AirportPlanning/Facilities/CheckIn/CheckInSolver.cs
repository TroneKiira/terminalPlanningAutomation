using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using Rhino;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities.CheckIn
{
    public class CheckInSolver : FacilitySolverBase
    {
        private readonly QueueOptimizer _optimizer;

        public CheckInSolver() : base()
        {
            _optimizer = new QueueOptimizer();
        }

        public CheckInResults Solve(CheckInInputs input)
        {
            CheckInResults finalResult = new CheckInResults();
            QueueResults results;
            int originalPhysical = input.NumberOfResources;

            if (input.OptimizeResources)
            {
                OptimizationResult opt = _optimizer.Optimize(
                        input,
                        SolveWithoutOptimization,
                        (i, c) => i.NumberOfResources = c,
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
