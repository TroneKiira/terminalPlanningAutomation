using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facilities.Security
{
    public class SecurityInputs
    {
        public List<Passenger> Passengers { get; set; }

        public ServiceProfile ServiceProfile { get; set; }

        /// <summary>
        /// Channels this facility serves. Empty means the legacy predicate
        /// (all passengers) applies; non-empty filters on the passenger channel.
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>Optional per-passenger service-time mapping.</summary>
        public IServiceTimeProvider ServiceTimeProvider { get; set; }

        /// <summary>
        /// Physical security lanes per channel. Used when optimization is
        /// switched off; a single value broadcasts to every channel.
        /// </summary>
        public List<int> PhysicalResources { get; set; } = new List<int>();

        /// <summary>Target 95th-percentile wait per channel, aligned with <see cref="PhysicalResources"/>.</summary>
        public List<double> TargetWait95 { get; set; } = new List<double>();

        /// <summary>Inclusive bounds for the optimizer's physical resource search.</summary>
        public ResourceSearchRange ResourceDomain { get; set; } =
            new ResourceSearchRange(1, ChannelResourcePlanResolver.DefaultMaxResources);

        /// <summary>
        /// Keyed pool plans (PNR, PTW, PDomain per pool). When supplied, these
        /// take precedence over the legacy PhysicalResources/TargetWait95 lists
        /// and are matched to resolved pools by Channel identity.
        /// </summary>
        public List<PoolPlan> PoolPlans { get; set; } = new List<PoolPlan>();

        /// <summary>
        /// Channel identity that receives transfer flows whose scenario rules
        /// set SecurityRouting to Separate. Configure a matching channel (and
        /// pool plan) to give them a dedicated transfer-security pool; without
        /// one they fall back to the shared pool and a warning is raised.
        /// </summary>
        public string TransferChannelName { get; set; } = "Transfer Security";

        public bool OptimizeResources { get; set; }

        public int Seed { get; set; }

        // NEW: Add Staffing Percentage
        public double StaffingPercentage { get; set; } = 100.0;

        public SecurityInputs()
        {
            Passengers = new List<Passenger>();
        }
    }
}
