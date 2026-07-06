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
            // --- 1. MACRO MATH (Calculate Gates) ---
            double eligiblePct = input.EligiblePercentage > 1.0 ? input.EligiblePercentage / 100.0 : input.EligiblePercentage;
            double utilization = input.DesignUtilization > 1.0 ? input.DesignUtilization / 100.0 : input.DesignUtilization;

            int passengerCount = (int)Math.Round(input.Passengers.Count * eligiblePct);
            double effectiveCapacity = input.ThroughputPerGate * utilization;
            if (effectiveCapacity <= 0) effectiveCapacity = 1.0;

            int gates = (int)Math.Ceiling(passengerCount / effectiveCapacity);

            // --- 2. MICRO FLOW (Pass Passengers Through) ---
            // Convert throughput (pax/hr) to service time in minutes (e.g., 300 pax/hr = 0.2 mins/pax)
            double serviceTimeMinutes = 60.0 / input.ThroughputPerGate;

            foreach (Passenger p in input.Passengers)
            {
                // Simple pass-through without queue simulation
                p.StartServiceTime = p.ArrivalTime;
                p.ServiceTime = serviceTimeMinutes;
                p.EndServiceTime = p.StartServiceTime + p.ServiceTime;
                p.WaitTime = 0.0;
                p.AssignedCounter = 0; // Not assigning a specific gate to keep it simple

                // Save to history
                p.History[FacilityType.PassengerVerification] = new FacilityRecord
                {
                    ArrivalTime = p.ArrivalTime,
                    StartServiceTime = p.StartServiceTime,
                    EndServiceTime = p.EndServiceTime,
                    WaitTime = p.WaitTime,
                    AssignedResource = p.AssignedCounter
                };

                // Advance their clock to prepare for the next facility (like Security)
                p.ArrivalTime = p.EndServiceTime + p.WalkingTime;
                p.QueueArrivalTime = p.ArrivalTime;
            }

            return new PassengerVerificationResults
            {
                Passengers = input.Passengers, // Pass them along!
                EligiblePassengers = passengerCount,
                RequiredGates = gates,
                Utilization = utilization,
                GateCapacity = effectiveCapacity
            };
        }
    }
}
