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
        /// Routed channels that have physical Check-In resources. Passenger
        /// assignment to these channels comes from Channel Routing and is
        /// already stored on Passenger.Channel. Empty means the legacy shared
        /// pool behavior.
        /// </summary>
        public List<string> Channels { get; set; } = new List<string>();

        /// <summary>
        /// Optional CHR output. When Channels is empty, its configured route
        /// channels define the physical pools so names do not need to be
        /// re-entered. Virtual Web routes are skipped, and Bag Drop is a
        /// separate facility stage rather than a Check-In channel.
        /// </summary>
        public ProfileChannelMap ChannelRouting { get; set; }

        /// <summary>
        /// Routed channel treated as staffed check-in counters. This channel is
        /// automatically subdivided by each passenger's DHPType and ProfileName.
        /// </summary>
        public string CounterChannel { get; set; } = DefaultChannelAssigner.CounterChannel;

        /// <summary>Optional nominal physical resource counts aligned with the expanded pools.</summary>
        public List<int> PoolPhysicalResources { get; set; } = new List<int>();

        /// <summary>Optional Wait95 targets aligned with the expanded pools.</summary>
        public List<double> PoolTargetWait95 { get; set; } = new List<double>();

        /// <summary>Optional minimum resource counts aligned with the expanded pools.</summary>
        public List<int> PoolMinimumResources { get; set; } = new List<int>();

        /// <summary>Optional maximum resource counts aligned with the expanded pools.</summary>
        public List<int> PoolMaximumResources { get; set; } = new List<int>();

        /// <summary>
        /// Keyed pool plans (PNR, PTW, PDomain per pool). When supplied, these
        /// take precedence over the legacy parallel Pool* lists and are matched
        /// to resolved pools by Channel|PoolKey identity.
        /// </summary>
        public List<PoolPlan> PoolPlans { get; set; } = new List<PoolPlan>();

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
