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

        public CheckInSolver()
            : base()
        {
            _optimizer = new QueueOptimizer();
        }

        public QueueResults Solve(CheckInInputs input)
        {
            QueueResults results;

            if (input.OptimizeResources)
            {
                OptimizationResult optimization =
                    _optimizer.Optimize(
                        input,
                        SolveWithoutOptimization,
                        (i, c) => i.NumberOfResources = c,
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

            // NEW: Save the historical record for Check-In
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
            return results;
        }

        private QueueResults SolveWithoutOptimization(
            CheckInInputs input)
        {
            RhinoApp.WriteLine($"Total Passengers: {input.Passengers.Count}");

            RhinoApp.WriteLine(
                $"Requires Check-In: {input.Passengers.Count(p => p.RequiresCheckIn)}");

            RhinoApp.WriteLine(
                $"Resources: {input.NumberOfResources}");

            RhinoApp.WriteLine(
                $"Mean Service: {input.ServiceProfile.MeanServiceTime}");

            RhinoApp.WriteLine(
                $"Std Dev: {input.ServiceProfile.StandardDeviation}");
            return ExecuteFacility(
                input.Passengers,
                p => p.RequiresCheckIn,
                input.NumberOfResources,
                input.ServiceProfile,
                new Random(input.Seed));
        }
    }
}
