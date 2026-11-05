using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.Transfer
{
    /// <summary>
    /// Inputs for the arrival-side baggage recheck queue. Following the
    /// TypeScript model, the recheck counter is the desk facility for the
    /// International→Domestic flow: passengers who require recheck are served
    /// here and are not also queued at the transfer desk.
    /// </summary>
    public class BaggageRecheckInputs
    {
        public List<Passenger> Passengers { get; set; } = new List<Passenger>();

        public ServiceProfile ServiceProfile { get; set; }

        /// <summary>Optional per-passenger service-time map, keyed by the
        /// per-flow pool names the Pool Names output reports.</summary>
        public IServiceTimeProvider ServiceTimeProvider { get; set; }

        /// <summary>
        /// Physical recheck positions per flow pool. Used when optimization is
        /// off; a single value broadcasts to every pool.
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
        /// flow separately). When off, all recheck-required transfers share a
        /// single pool.
        /// </summary>
        public bool SplitPoolsPerFlow { get; set; } = true;

        public bool OptimizeResources { get; set; }

        public int Seed { get; set; }

        public double StaffingPercentage { get; set; } = 100.0;
    }

    /// <summary>
    /// Results of the arrival-side baggage recheck queue: the full merged
    /// population, pool-level queue results and per-flow position counts.
    /// </summary>
    public class BaggageRecheckResults
    {
        /// <summary>Aggregate physical recheck positions across every pool.</summary>
        public int PhysicalPositions { get; set; }

        /// <summary>Aggregate staffed recheck positions actually simulated.</summary>
        public int ActivePositions { get; set; }

        /// <summary>Number of transfer passengers the recheck queue served.</summary>
        public int ServedCount { get; set; }

        /// <summary>Transfer passengers whose rules bypassed baggage recheck.</summary>
        public int BypassedCount { get; set; }

        public QueueResults QueueResults { get; set; }

        /// <summary>Pool names in output order: one per transfer flow, or "Shared".</summary>
        public List<string> PoolNames { get; set; } = new List<string>();

        /// <summary>Nominal (pre-optimization) physical positions per pool.</summary>
        public List<int> NominalPerChannel { get; set; } = new List<int>();

        public List<int> PhysicalPerChannel { get; set; } = new List<int>();

        public List<int> ActivePerChannel { get; set; } = new List<int>();

        /// <summary>Whole-run 95th percentile wait per pool, in minutes.</summary>
        public Dictionary<string, double> Wait95PerChannel { get; set; } =
            new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>Full metrics of each pool, in plan order.</summary>
        public List<QueueResults> PoolResults { get; set; } = new List<QueueResults>();

        /// <summary>Optimizer search outcomes per pool, for charting.</summary>
        public List<ResourceSearchOutcome> SearchOutcomes { get; set; } =
            new List<ResourceSearchOutcome>();
    }
}
