using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// Performance statistics for one passenger group — a channel or a passenger
    /// profile — within a whole simulation, or within one demand step.
    /// The key is whatever the group is named (channel name or profile name).
    /// </summary>
    public class ClassMetrics
    {
        /// <summary>Group name (channel name or profile name).</summary>
        public string Key { get; set; } = string.Empty;

        public int Count { get; set; }

        public double AverageWait { get; set; }

        public double MaxWait { get; set; }

        public double Wait95 { get; set; }

        public int PeakQueueLength { get; set; }
    }
}
