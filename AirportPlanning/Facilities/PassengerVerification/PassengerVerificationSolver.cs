using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities.PassengerVerification
{
    public class PassengerVerificationSolver
    {
        public PassengerVerificationResults Solve(PassengerVerificationInputs input)
        {
            double eligiblePct = input.EligiblePercentage > 1.0 ? input.EligiblePercentage / 100.0 : input.EligiblePercentage;
            double utilization = input.DesignUtilization > 1.0 ? input.DesignUtilization / 100.0 : input.DesignUtilization;

            // Size gates for the design hour when windowed; otherwise use the
            // whole (non-warm-up) population for backwards compatibility.
            bool windowed = input.Passengers.Any(p => p.IsDesignStep);

            List<Passenger> basis = windowed
                ? input.Passengers.Where(p => p.IsDesignStep && !p.IsWarmUp).ToList()
                : input.Passengers.Where(p => !p.IsWarmUp).ToList();

            int passengerCount = (int)Math.Round(basis.Count * eligiblePct);
            double effectiveCapacity = input.ThroughputPerGate * utilization;
            if (effectiveCapacity <= 0) effectiveCapacity = 1.0;

            int gates = (int)Math.Ceiling(passengerCount / effectiveCapacity);

            double serviceTimeMinutes = 60.0 / input.ThroughputPerGate;

            foreach (Passenger p in input.Passengers)
            {
                p.StartServiceTime = p.ArrivalTime;
                p.ServiceTime = serviceTimeMinutes;
                p.EndServiceTime = p.StartServiceTime + p.ServiceTime;
                p.WaitTime = 0.0;
                p.AssignedCounter = 0;

                p.History[FacilityType.PassengerVerification] = new FacilityRecord
                {
                    ArrivalTime = p.ArrivalTime,
                    StartServiceTime = p.StartServiceTime,
                    EndServiceTime = p.EndServiceTime,
                    WaitTime = p.WaitTime,
                    AssignedResource = p.AssignedCounter
                };

                p.ArrivalTime = p.EndServiceTime + p.WalkingTime;
                p.QueueArrivalTime = p.ArrivalTime;
            }

            return new PassengerVerificationResults
            {
                Passengers = input.Passengers,
                EligiblePassengers = passengerCount,
                RequiredGates = gates,

                // 🚨 THE FIX: Save the baseline so the Reporter can compare it!
                BaselineProcessorCount = gates,

                Utilization = utilization,

                // 🚨 THE FIX: Pass the RAW throughput (e.g. 300) so the Sizer can recalculate utilization on override
                GateCapacity = input.ThroughputPerGate
            };
        }
    }
}
