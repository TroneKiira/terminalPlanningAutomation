using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities
{
    public class BagDropInputs
    {
        public List<Passenger> Passengers { get; set; }
        public ServiceProfile ServiceProfile { get; set; }
        public int NumberOfResources { get; set; }
        public bool OptimizeResources { get; set; }
        public double TargetMaximumWait { get; set; }
        public int Seed { get; set; }

        // NEW: Add Staffing Percentage to match the optimizer's requirements
        public double StaffingPercentage { get; set; } = 100.0;

        public BagDropInputs()
        {
            Passengers = new List<Passenger>();
        }
    }
}
