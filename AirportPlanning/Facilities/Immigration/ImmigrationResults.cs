using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.Immigration
{
    public class ImmigrationResults
    {
        /// <summary>Aggregate physical positions across every pool.</summary>
        public int PhysicalPositions { get; set; }

        /// <summary>Aggregate staffed positions actually simulated.</summary>
        public int ActivePositions { get; set; }

        public QueueResults QueueResults { get; set; }

        /// <summary>Pool names in output order, including plan-derived subpools.</summary>
        public List<string> PoolNames { get; set; } = new List<string>();

        /// <summary>Nominal physical resources per pool, in output order.</summary>
        public List<int> NominalPerChannel { get; set; } = new List<int>();

        /// <summary>Physical positions per dedicated channel pool (empty for a shared pool).</summary>
        public List<int> PhysicalPerChannel { get; set; } = new List<int>();

        /// <summary>Staffed positions per dedicated channel pool.</summary>
        public List<int> ActivePerChannel { get; set; } = new List<int>();

        /// <summary>Wait95 per dedicated channel pool.</summary>
        public Dictionary<string, double> Wait95PerChannel { get; set; } =
            new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>Full metrics of each pool, in plan order.</summary>
        public List<QueueResults> PoolResults { get; set; } = new List<QueueResults>();

        /// <summary>Optimizer search outcomes with trace data for charting.</summary>
        public List<ResourceSearchOutcome> SearchOutcomes { get; set; } = new List<ResourceSearchOutcome>();
    }
}
