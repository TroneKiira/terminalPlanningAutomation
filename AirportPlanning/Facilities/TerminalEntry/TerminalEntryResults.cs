using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using System.Collections.Generic;

namespace AirportPlanning.Facilities.TerminalEntry
{
    public class TerminalEntryResults
    {
        public QueueResults QueueResults { get; set; }
        public int PhysicalPositions { get; set; }
        public int ActivePositions { get; set; }
        public List<string> PoolNames { get; set; } = new List<string>();
        public List<int> PhysicalPerPool { get; set; } = new List<int>();
        public List<int> ActivePerPool { get; set; } = new List<int>();
        public List<int> NominalPerPool { get; set; } = new List<int>();
        public Dictionary<string, double> Wait95PerPool { get; set; } =
            new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase);
        public List<double> AverageWaitPerPool { get; set; } = new List<double>();
        public List<double> MaxWaitPerPool { get; set; } = new List<double>();
        public List<double> Wait95ListPerPool { get; set; } = new List<double>();
        public List<int> PeakQueuePerPool { get; set; } = new List<int>();
        public List<ResourceSearchOutcome> SearchOutcomes { get; set; } = new List<ResourceSearchOutcome>();
    }
}
