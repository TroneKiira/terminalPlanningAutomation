using System;
using System.Collections.Generic;
using System.Globalization;

namespace AirportPlanning.Core.Models
{
    /// <summary>
    /// One server pool's own queue history and metrics, kept alongside the
    /// merged facility result.
    ///
    /// Dedicated channel/sector queues are simulated independently, then their
    /// passengers are merged into one facility-level <see cref="QueueResults"/>.
    /// The merge loses the per-pool shape of the queue, so this snapshot is what
    /// lets a chart show "Counter|DomesticDeparture|Economy" as its own queue
    /// instead of folding it into the facility total.
    ///
    /// Timelines and cumulative flows are copied from the pool's own result;
    /// they are not reconstructed from the merged data.
    /// </summary>
    public class QueuePoolResult
    {
        /// <summary>
        /// Pool identity in the same form the solver publishes:
        /// "Counter|DomesticDeparture|Economy", a bare channel name, or
        /// "Shared" for a single facility-wide queue.
        /// </summary>
        public string Name { get; set; } = "Shared";

        /// <summary>Channel part of <see cref="Name"/> (empty for a shared pool).</summary>
        public string Channel { get; set; } = string.Empty;

        /// <summary>
        /// Pool key part of <see cref="Name"/> ("DHPType|ProfileName"), or empty
        /// when the pool serves its whole channel.
        /// </summary>
        public string PoolKey { get; set; } = string.Empty;

        /// <summary>True when this pool serves every passenger in one queue.</summary>
        public bool IsShared { get; set; }

        /// <summary>Physical resources allotted to this pool.</summary>
        public int PhysicalResources { get; set; }

        /// <summary>Staffed (active) resources this pool actually simulated.</summary>
        public int ActiveResources { get; set; }

        /// <summary>Target Wait95 in minutes (0 when none was configured).</summary>
        public double TargetWait95 { get; set; }

        /// <summary>True when a positive target was configured for this pool.</summary>
        public bool TargetApplied
        {
            get { return TargetWait95 > 0.0; }
        }

        /// <summary>True when the pool met its target, or no target applied.</summary>
        public bool TargetMet
        {
            get { return !TargetApplied || Wait95 <= TargetWait95 + 1e-9; }
        }

        /// <summary>
        /// Passengers the pool served, excluding warm-up, so the count matches
        /// the queue timeline and Wait95 it is shown beside.
        /// </summary>
        public int PassengerCount { get; set; }

        /// <summary>Passengers who actually had to wait (WaitTime greater than zero).</summary>
        public int WaitingPassengerCount { get; set; }

        /// <summary>Earliest arrival in this pool, or NaN when nobody was served.</summary>
        public double FirstArrivalTime { get; set; } = double.NaN;

        /// <summary>Latest arrival in this pool, or NaN when nobody was served.</summary>
        public double LastArrivalTime { get; set; } = double.NaN;

        /// <summary>
        /// First recorded queue event (a passenger joining the queue), or NaN
        /// when every passenger was served on arrival.
        /// </summary>
        public double FirstQueueEventTime { get; set; } = double.NaN;

        /// <summary>
        /// Last recorded queue event (a passenger starting service), or NaN
        /// when every passenger was served on arrival.
        /// </summary>
        public double LastQueueEventTime { get; set; } = double.NaN;

        public double AverageWait { get; set; }

        public double MaxWait { get; set; }

        public double Wait95 { get; set; }

        public int PeakQueueLength { get; set; }

        public double PeakQueueTime { get; set; }

        public double SimulationDuration { get; set; }

        /// <summary>Queue length over time for this pool alone.</summary>
        public List<QueueTimelinePoint> QueueTimeline { get; set; } =
            new List<QueueTimelinePoint>();

        /// <summary>Cumulative arrivals/departures for this pool alone.</summary>
        public List<QueueCumulativePoint> CumulativeFlow { get; set; } =
            new List<QueueCumulativePoint>();

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} [PNR={1}, PTW={2:0.##}, W95={3:0.##}]",
                Name,
                PhysicalResources,
                TargetWait95,
                Wait95);
        }
    }
}
