using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Models
{
    public class FacilityRecord
    {
        public double ArrivalTime { get; set; }
        public double StartServiceTime { get; set; }
        public double EndServiceTime { get; set; }
        public double WaitTime { get; set; }
        public int AssignedResource { get; set; }

        // NEW: Deep clone method
        public FacilityRecord Clone()
        {
            return new FacilityRecord
            {
                ArrivalTime = this.ArrivalTime,
                StartServiceTime = this.StartServiceTime,
                EndServiceTime = this.EndServiceTime,
                WaitTime = this.WaitTime,
                AssignedResource = this.AssignedResource
            };
        }
    }

    public enum FacilityType
    {
        CheckIn,
        BagDrop,
        Security,
        Immigration,
        PassengerVerification,
        DepartureLounge, // NEW: Added for retail dwell time
        Boarding
    }
}
