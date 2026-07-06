using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AirportPlanning.Facilities.CheckIn
{
    public class CheckInInputs
    {
        public List<Passenger> Passengers { get; set; }
        public ServiceProfile ServiceProfile { get; set; }

        // Represents the physical number of counters (N)
        public int NumberOfResources { get; set; }

        public int Seed { get; set; }
        public bool OptimizeResources { get; set; }
        public double TargetMaximumWait { get; set; }

        // NEW: Add Staffing Percentage matching your GH Script
        public double StaffingPercentage { get; set; } = 100.0;

        public CheckInInputs()
        {
            Passengers = new List<Passenger>();
        }
    }


       
    
    public class ArrivalProfile
    {
        public double Peak { get; set; }

        public double Sigma { get; set; }
    }
}
