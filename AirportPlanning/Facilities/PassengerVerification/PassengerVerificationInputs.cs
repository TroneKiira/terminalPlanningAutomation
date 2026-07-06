using AirportPlanning.Core.Inputs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities.PassengerVerification
{
    public class PassengerVerificationInputs
    {
        public PassengerDemand PassengerDemand { get; set; }

        public double EligiblePercentage { get; set; }

        public double ThroughputPerGate { get; set; }   // passengers/hour

        public double DesignUtilization { get; set; }   // e.g. 0.85
    }
}
