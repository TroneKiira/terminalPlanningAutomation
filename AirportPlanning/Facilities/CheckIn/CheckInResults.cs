using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.CheckIn
{
    public class CheckInResults
    {
        /// <summary>Aggregate physical counters across every pool.</summary>
        public int PhysicalCounters { get; set; }

        /// <summary>Aggregate staffed counters actually simulated.</summary>
        public int ActiveCounters { get; set; }

        /// <summary>Pool names in output order: each dedicated channel (or
        /// "Channel|PoolKey" when subdivided), or "Shared" for a single pool.</summary>
        public List<string> PoolNames { get; set; } = new List<string>();

        /// <summary>Nominal physical resources per pool, in output order.</summary>
        public List<int> NominalPerChannel { get; set; } = new List<int>();

        /// <summary>Unique channel names and aggregate physical/staffed counts, excluding pool subkeys.</summary>
        public List<string> ChannelNames { get; set; } = new List<string>();
        public List<int> PhysicalPerChannelTotal { get; set; } = new List<int>();
        public List<int> ActivePerChannelTotal { get; set; } = new List<int>();

        public QueueResults QueueResults { get; set; }

        /// <summary>Physical counters per dedicated channel pool (empty for a shared pool).</summary>
        public List<int> PhysicalPerChannel { get; set; } = new List<int>();

        /// <summary>Staffed counters per dedicated channel pool.</summary>
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
