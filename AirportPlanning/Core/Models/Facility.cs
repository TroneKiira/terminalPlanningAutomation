using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Models
{
    public class FacilityRecord
    {
        // FIX: Changed to double to match passenger precision
        public double ArrivalTime { get; set; }
        public double StartServiceTime { get; set; }
        public double EndServiceTime { get; set; }
        public double WaitTime { get; set; }
        public int AssignedResource { get; set; }
    }

    public enum FacilityType
    {
        CheckIn,
        BagDrop,
        Security,
        Immigration,
        PassengerVerification,
        Boarding
    }
}
