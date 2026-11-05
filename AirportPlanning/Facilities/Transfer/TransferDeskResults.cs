using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.Transfer
{
    /// <summary>
    /// Results of the arrival-side transfer desk queue: the full merged
    /// population, pool-level queue results and per-flow position counts.
    /// </summary>
    public class TransferDeskResults
    {
        /// <summary>Aggregate physical desk positions across every pool.</summary>
        public int PhysicalPositions { get; set; }

        /// <summary>Aggregate staffed desk positions actually simulated.</summary>
        public int ActivePositions { get; set; }

        /// <summary>Number of transfer passengers the desk queue served.</summary>
        public int ServedCount { get; set; }

        /// <summary>Transfer passengers whose rules bypassed the desk.</summary>
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
