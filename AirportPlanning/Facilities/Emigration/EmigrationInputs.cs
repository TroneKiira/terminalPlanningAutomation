using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AirportPlanning.Facilities.Emigration
{
    public class EmigrationInputs
    {
        public List<Passenger> Passengers { get; set; }

        public ServiceProfile ServiceProfile { get; set; }

        /// <summary>
        /// Share of international departing passengers routed to the e-gate
        /// pool, from 0 to 100. The remainder use manual counters.
        /// </summary>
        public double EGateSharePercentage { get; set; } = 40.0;

        /// <summary>
        /// Channels this facility serves. Empty means the legacy predicate
        /// (international departures) applies; non-empty filters on the channel.
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>Optional per-passenger service-time mapping.</summary>
        public IServiceTimeProvider ServiceTimeProvider { get; set; }

        /// <summary>
        /// Physical emigration positions per channel. Used when optimization is
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
        /// set EmigrationRouting to Separate. Configure a matching channel (and
        /// pool plan) to give them a dedicated transfer-emigration pool; without
        /// one they fall back to the shared hall and a warning is raised. Such
        /// passengers are excluded from the e-gate/counter route split.
        /// </summary>
        public string TransferChannelName { get; set; } = "Transfer Emigration";

        public bool OptimizeResources { get; set; }

        public int Seed { get; set; }

        public double StaffingPercentage { get; set; } = 100.0;

        public EmigrationInputs()
        {
            Passengers = new List<Passenger>();
        }
    }
}
