using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.Security
{
    public class SecurityResults
    {
        /// <summary>Aggregate physical lanes across every pool.</summary>
        public int PhysicalLanes { get; set; }

        /// <summary>Aggregate staffed lanes actually simulated.</summary>
        public int ActiveLanes { get; set; }

        /// <summary>Alias kept for callers that speak in counters.</summary>
        public int PhysicalCounters
        {
            get { return PhysicalLanes; }
            set { PhysicalLanes = value; }
        }

        /// <summary>Alias kept for callers that speak in counters.</summary>
        public int ActiveCounters
        {
            get { return ActiveLanes; }
            set { ActiveLanes = value; }
        }

        public QueueResults QueueResults { get; set; }

        /// <summary>Physical lanes per dedicated channel pool (empty for a shared pool).</summary>
        public List<int> PhysicalPerChannel { get; set; } = new List<int>();

        /// <summary>Staffed lanes per dedicated channel pool.</summary>
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
