using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities.PassengerVerification
{
    public class PassengerVerificationResults
    {
        public List<Passenger> Passengers { get; set; }
        public int EligiblePassengers { get; set; }
        public int RequiredGates { get; set; }
        public double Utilization { get; set; }
        public double GateCapacity { get; set; }

        // 🚨 NEW: Carry the baseline down the wire!
        public int BaselineProcessorCount { get; set; }
    }
}
