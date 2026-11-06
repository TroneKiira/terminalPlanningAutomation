using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.Customs
{
    /// <summary>
    /// Inputs for the arrival-side customs hand-baggage stage.
    ///
    /// One facility, one passenger class. There is deliberately no transfer
    /// channel: customs does not segregate transfer passengers from
    /// originating arrivals, so one shared pool is the normal setup.
    /// </summary>
    public class CustomsInputs
    {
        public List<Passenger> Passengers { get; set; } = new List<Passenger>();

        public ServiceProfile ServiceProfile { get; set; }

        /// <summary>
        /// Optional X-ray position identities matching Passenger.Channel.
        /// Empty keeps one shared pool. International→International transfers
        /// remain airside and are never served, even when a channel is wired.
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>Optional per-passenger service-time mapping.</summary>
        public IServiceTimeProvider ServiceTimeProvider { get; set; }

        /// <summary>
        /// Physical customs positions per channel. Used when optimization is
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

        public bool OptimizeResources { get; set; }

        public int Seed { get; set; }

        public double StaffingPercentage { get; set; } = 100.0;
    }
}
