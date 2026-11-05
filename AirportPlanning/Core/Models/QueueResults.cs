using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Models
{
    public class QueueResults
    {
        /// <summary>
        /// Complete passenger population after simulation.
        /// </summary>
        public List<Passenger> Passengers { get; set; }

        /// <summary>
        /// Only passengers processed by this facility.
        /// </summary>
        public List<Passenger> ProcessedPassengers { get; set; }

        /// <summary>
        /// Queue resources used during simulation.
        /// </summary>
        public List<QueueResource> Resources { get; set; }

        /// <summary>
        /// Server pools behind this result, in resource-list order. Each pool's
        /// processor IDs restart at zero, so this is what keeps dedicated
        /// channel pools apart when the results are merged.
        /// </summary>
        public List<ProcessorPoolInfo> ProcessorPools { get; set; } =
            new List<ProcessorPoolInfo>();

        public int ResourcesUsed { get; set; }

        public double SimulationDuration { get; set; }

        public int PeakQueueLength { get; set; }

        public double AverageWait { get; set; }

        public double MaxWait { get; set; }

        public double Wait95 { get; set; }

        public List<double> CounterUtilization { get; set; }

        public int BaselineProcessorCount { get; set; } = -1;

        public int PhysicalProcessorCount { get; set; } = -1;

        /// <summary>
        /// Absolute simulation minute at which the global peak queue occurred.
        /// </summary>
        public double PeakQueueTime { get; set; }

        /// <summary>
        /// Demand step in which the global peak queue occurred (-1 when unknown).
        /// </summary>
        public int PeakQueueStepIndex { get; set; } = -1;

        /// <summary>
        /// Design hour step index selected for optimization and reporting
        /// (-1 when not windowed).
        /// </summary>
        public int DesignStepIndex { get; set; } = -1;

        /// <summary>
        /// Number of queues stacked in series by a facility that simulates the
        /// same passengers through consecutive stages (for example security's
        /// e-gate stage feeding the ATRS stage). The pooled results then hold
        /// one cohort per stage, so the sum of pool passenger counts is
        /// <see cref="ProcessedPassengers"/> times this count, not once.
        /// Zero or one means a conventional result where every passenger is
        /// served exactly once.
        /// </summary>
        public int SeriesStageCount { get; set; }

        /// <summary>Duration of the warm-up segment in minutes.</summary>
        public double WarmUpMinutes { get; set; }

        /// <summary>
        /// Per-step performance statistics for windowed simulations. Warm-up
        /// passengers are excluded.
        /// </summary>
        public List<IntervalQueueMetrics> IntervalMetrics { get; set; }

        /// <summary>
        /// Whole-run performance broken down by passenger channel
        /// (keyed by channel name). Warm-up passengers are excluded.
        /// </summary>
        public Dictionary<string, ClassMetrics> ByChannel { get; set; } =
            new Dictionary<string, ClassMetrics>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whole-run performance broken down by passenger profile
        /// (keyed by profile name). Warm-up passengers are excluded.
        /// </summary>
        public Dictionary<string, ClassMetrics> ByProfile { get; set; } =
            new Dictionary<string, ClassMetrics>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Queue length after each queue event, i.e. the queue building and
        /// draining over the reported window. Warm-up passengers are excluded,
        /// so the series starts from an empty queue.
        /// </summary>
        public List<QueueTimelinePoint> QueueTimeline { get; set; } =
            new List<QueueTimelinePoint>();

        /// <summary>
        /// Cumulative arrivals against cumulative departures, sampled onto a
        /// regular time grid. The gap between the two is the queue. Warm-up
        /// passengers are excluded.
        /// </summary>
        public List<QueueCumulativePoint> CumulativeFlow { get; set; } =
            new List<QueueCumulativePoint>();

        /// <summary>
        /// Per-pool queue history behind this facility result, in solver pool
        /// order. Each entry keeps the pool's own timeline, cumulative flow and
        /// metrics, so charts can separate independent channel/sector queues
        /// that the facility-level aggregate otherwise folds together.
        ///
        /// Empty when the producer did not record pool detail.
        /// </summary>
        public List<QueuePoolResult> PoolResults { get; set; } =
            new List<QueuePoolResult>();

        public QueueResults()
        {
            Passengers =
                new List<Passenger>();

            ProcessedPassengers =
                new List<Passenger>();

            Resources =
                new List<QueueResource>();

            ProcessorPools =
                new List<ProcessorPoolInfo>();

            CounterUtilization =
                new List<double>();

            IntervalMetrics =
                new List<IntervalQueueMetrics>();

            QueueTimeline =
                new List<QueueTimelinePoint>();

            CumulativeFlow =
                new List<QueueCumulativePoint>();
        }

        /// <summary>
        /// Design-hour diagnostic: the Wait95 of the design-hour step when the
        /// run is windowed, otherwise the whole-run value. The resource
        /// optimizer does NOT use this; it sizes against the whole-run
        /// <see cref="Wait95"/> so the enforced target matches the reported W95.
        /// </summary>
        public double GetEvaluationWait95()
        {
            if (DesignStepIndex >= 0 && IntervalMetrics != null)
            {
                foreach (IntervalQueueMetrics metric in IntervalMetrics)
                {
                    if (metric.StepIndex == DesignStepIndex)
                        return metric.Wait95;
                }
            }

            return Wait95;
        }

        /// <summary>
        /// Wait95 for one channel or profile. Prefers the design-hour step when
        /// windowed; falls back to the whole-run value.
        /// </summary>
        public double GetEvaluationWait95(string key, bool byChannel = false)
        {
            if (string.IsNullOrEmpty(key))
                return GetEvaluationWait95();

            if (DesignStepIndex >= 0 && IntervalMetrics != null)
            {
                foreach (IntervalQueueMetrics metric in IntervalMetrics)
                {
                    if (metric.StepIndex != DesignStepIndex)
                        continue;

                    Dictionary<string, ClassMetrics> map =
                        byChannel ? metric.ByChannel : metric.ByProfile;

                    if (map != null && map.TryGetValue(key, out ClassMetrics stepValue) && stepValue.Count > 0)
                        return stepValue.Wait95;
                }
            }

            Dictionary<string, ClassMetrics> overall =
                byChannel ? ByChannel : ByProfile;

            if (overall != null && overall.TryGetValue(key, out ClassMetrics value) && value.Count > 0)
                return value.Wait95;

            return Wait95;
        }
    }
}
