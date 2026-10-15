using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// Performance statistics for a single time step of a windowed demand
    /// simulation. Warm-up steps are typically reported separately and excluded
    /// from design comparisons.
    /// </summary>
    public class IntervalQueueMetrics
    {
        /// <summary>Zero-based time step index (-1 for warm-up).</summary>
        public int StepIndex { get; set; } = -1;

        /// <summary>Start of the step in absolute simulation minutes.</summary>
        public double StepStartMinute { get; set; }

        /// <summary>End of the step in absolute simulation minutes.</summary>
        public double StepEndMinute { get; set; }

        /// <summary>True when this step is the pre-window warm-up segment.</summary>
        public bool IsWarmUp { get; set; }

        /// <summary>Number of passengers whose demand step is this step.</summary>
        public int ArrivalCount { get; set; }

        /// <summary>Number of passengers processed in this step.</summary>
        public int ProcessedCount { get; set; }

        /// <summary>Average waiting time for this step's cohort (minutes).</summary>
        public double AverageWait { get; set; }

        /// <summary>Maximum waiting time for this step's cohort (minutes).</summary>
        public double MaxWait { get; set; }

        /// <summary>95th percentile waiting time for this step's cohort (minutes).</summary>
        public double Wait95 { get; set; }

        /// <summary>
        /// Peak queue length observed while this step's cohort is being served.
        /// </summary>
        public int PeakQueueLength { get; set; }

        /// <summary>
        /// Absolute minute at which this step's peak queue was observed.
        /// </summary>
        public double PeakQueueTime { get; set; }

        /// <summary>Passengers per hour serviced within this step.</summary>
        public double Throughput { get; set; }

        /// <summary>
        /// Performance for this step broken down by passenger channel
        /// (keyed by channel name).
        /// </summary>
        public Dictionary<string, ClassMetrics> ByChannel { get; set; } =
            new Dictionary<string, ClassMetrics>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Performance for this step broken down by passenger profile
        /// (keyed by profile name).
        /// </summary>
        public Dictionary<string, ClassMetrics> ByProfile { get; set; } =
            new Dictionary<string, ClassMetrics>(StringComparer.OrdinalIgnoreCase);
    }
}
