using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace AirportPlanning.Facilities.Immigration
{
    public class ImmigrationInputs
    {
        public List<Passenger> Passengers { get; set; }

        public ServiceProfile ServiceProfile { get; set; }

        public int NumberOfResources { get; set; }

        public bool OptimizeResources { get; set; }

        public double TargetMaximumWait { get; set; }

        public int Seed { get; set; }

        // NEW: Add Staffing Percentage
        public double StaffingPercentage { get; set; } = 100.0;

        public ImmigrationInputs()
        {
            Passengers = new List<Passenger>();
        }
    }
}
