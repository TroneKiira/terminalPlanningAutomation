using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
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

        /// <summary>
        /// Channels this facility serves. Empty means the legacy predicate
        /// (RequiresBagDrop) applies; non-empty filters on the passenger channel.
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>Optional per-passenger service-time mapping.</summary>
        public IServiceTimeProvider ServiceTimeProvider { get; set; }

        /// <summary>
        /// Physical bag-drop counters per channel. Used when optimization is
        /// switched off; a single value broadcasts to every channel.
        /// </summary>
        public List<int> PhysicalResources { get; set; } = new List<int>();

        /// <summary>Target 95th-percentile wait per channel, aligned with <see cref="PhysicalResources"/>.</summary>
        public List<double> TargetWait95 { get; set; } = new List<double>();

        /// <summary>Inclusive bounds for the optimizer's physical resource search.</summary>
        public ResourceSearchRange ResourceDomain { get; set; } =
            new ResourceSearchRange(1, ChannelResourcePlanResolver.DefaultMaxResources);

        public bool OptimizeResources { get; set; }
        public int Seed { get; set; }

        // NEW: Add Staffing Percentage to match the optimizer's requirements
        public double StaffingPercentage { get; set; } = 100.0;

        public BagDropInputs()
        {
            Passengers = new List<Passenger>();
        }
    }
}
