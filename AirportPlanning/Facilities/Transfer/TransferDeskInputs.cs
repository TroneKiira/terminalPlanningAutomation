using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.Transfer
{
    /// <summary>
    /// Inputs for the arrival-side transfer desk queue. Mirrors the facility
    /// component conventions: the full passenger population is supplied and
    /// only passengers whose scenario rules require the transfer desk are
    /// served; everyone else passes through untouched.
    /// </summary>
    public class TransferDeskInputs
    {
        public List<Passenger> Passengers { get; set; } = new List<Passenger>();

        public ServiceProfile ServiceProfile { get; set; }

        /// <summary>Optional per-passenger service-time map, keyed by the
        /// per-flow pool names the Pool Names output reports.</summary>
        public IServiceTimeProvider ServiceTimeProvider { get; set; }

        /// <summary>
        /// Physical desk positions per flow pool. Used when optimization is off;
        /// a single value broadcasts to every pool.
        /// </summary>
        public List<int> PhysicalResources { get; set; } = new List<int>();

        /// <summary>Target whole-run 95th percentile wait per pool in minutes.</summary>
        public List<double> TargetWait95 { get; set; } = new List<double>();

        /// <summary>Inclusive bounds for the optimizer's position search.</summary>
        public ResourceSearchRange ResourceDomain { get; set; } =
            new ResourceSearchRange(1, ChannelResourcePlanResolver.DefaultMaxResources);

        /// <summary>Keyed pool plans matched by the per-flow channel names.</summary>
        public List<PoolPlan> PoolPlans { get; set; } = new List<PoolPlan>();

        /// <summary>
        /// Size one pool per transfer flow (the TypeScript model sizes each
        /// flow separately). When off, all desk-required transfers share a
        /// single pool.
        /// </summary>
        public bool SplitPoolsPerFlow { get; set; } = true;

        public bool OptimizeResources { get; set; }

        public int Seed { get; set; }

        public double StaffingPercentage { get; set; } = 100.0;
    }
}
