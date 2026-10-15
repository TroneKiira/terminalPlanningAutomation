using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
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

        /// <summary>
        /// Channels this facility serves. Empty means the legacy predicate
        /// (RequiresCheckIn) applies; non-empty filters on the passenger channel.
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>Optional per-passenger service-time mapping.</summary>
        public IServiceTimeProvider ServiceTimeProvider { get; set; }

        /// <summary>
        /// Physical check-in counters per channel. Used when optimization is
        /// switched off. A single value broadcasts to every channel; when no
        /// channel is wired it is the shared-pool counter count.
        /// </summary>
        public List<int> PhysicalResources { get; set; } = new List<int>();

        /// <summary>
        /// Target 95th-percentile wait in minutes per channel, aligned with
        /// <see cref="PhysicalResources"/>.
        /// </summary>
        public List<double> TargetWait95 { get; set; } = new List<double>();

        /// <summary>Inclusive bounds for the optimizer's physical resource search.</summary>
        public ResourceSearchRange ResourceDomain { get; set; } =
            new ResourceSearchRange(1, ChannelResourcePlanResolver.DefaultMaxResources);

        public int Seed { get; set; }
        public bool OptimizeResources { get; set; }

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
