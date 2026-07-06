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
        public PassengerVerificationResults Solve(
         PassengerVerificationInputs input)
        {
            int passengers =
                (int)Math.Round(
                    input.PassengerDemand.DeparturePassengers *
                    input.EligiblePercentage);

            double effectiveCapacity =
                input.ThroughputPerGate *
                input.DesignUtilization;

            int gates =
                (int)Math.Ceiling(
                    passengers / effectiveCapacity);

            return new PassengerVerificationResults
            {
                EligiblePassengers = passengers,
                RequiredGates = gates,
                Utilization = input.DesignUtilization,
                GateCapacity = effectiveCapacity
            };
        }
    }
}
