using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Queueing
{
    public class QueueMetricsCalculator
    {
        public QueueResults Calculate(
            List<Passenger> passengers,
            List<QueueResource> counters)
        {
            QueueResults results = new QueueResults();

            results.Passengers = passengers;
            results.Resources = counters;

            // Warm-up passengers are simulated but excluded from reported metrics.
            List<Passenger> windowed = passengers == null
                ? new List<Passenger>()
                : passengers.Where(p => !p.IsWarmUp).ToList();

            GetPassengerMetrics(windowed, results);

            results.ByChannel = BuildGroupMetrics(
                windowed, p => string.IsNullOrEmpty(p.Channel) ? "(none)" : p.Channel);

            results.ByProfile = BuildGroupMetrics(
                windowed, p => string.IsNullOrEmpty(p.ProfileName) ? "(unassigned)" : p.ProfileName);

            List<QueueTimelinePoint> timeline = BuildQueueTimeline(windowed);

            // Exposed rather than discarded: this is the queue building and
            // draining over time, which the queue-mechanics chart plots.
            results.QueueTimeline = timeline;

            QueueTimelinePoint peak = GetPeakPoint(timeline);

            results.PeakQueueLength = peak != null ? peak.Queue : 0;
            results.PeakQueueTime = peak != null ? peak.Time : 0.0;

            results.CounterUtilization = GetCounterUtilization(windowed, counters);

            results.IntervalMetrics = GetIntervalMetrics(windowed, timeline);
            results.PeakQueueStepIndex = FindStepAtTime(results.IntervalMetrics, results.PeakQueueTime);

            results.CumulativeFlow = BuildCumulativeFlow(windowed);

            Passenger designPassenger = windowed.FirstOrDefault(p => p.IsDesignStep);

            if (designPassenger != null)
                results.DesignStepIndex = designPassenger.DemandStepIndex;

            if (windowed.Count > 0)
            {
                results.SimulationDuration =
                    windowed.Max(p => p.EndServiceTime) -
                    windowed.Min(p => p.ArrivalTime);
            }
            else
            {
                results.SimulationDuration = 0.0;
            }

            return results;
        }

        /// <summary>
        /// Lightweight whole-run Wait95 used by the resource optimizer while
        /// searching: the 95th percentile over every non-warm-up wait in the
        /// pool, matching the reported whole-run <see cref="QueueResults.Wait95"/>.
        ///
        /// The design-step argument is kept for callers that need a scoped
        /// diagnostic (design-step waits when >= 0 with a warm-up-free fallback
        /// to the whole run); the optimizer itself always passes -1 so the
        /// target it enforces is exactly the W95 shown in the outputs.
        ///
        /// It deliberately skips group metrics, the queue timeline, counter
        /// utilization and the per-step <see cref="IntervalQueueMetrics"/>, which
        /// dominate the cost of a full <see cref="Calculate"/> and are not needed
        /// to judge whether a candidate capacity meets the target wait.
        /// </summary>
        public double EvaluateWaitOnly(List<Passenger> passengers, int designStepIndex)
        {
            if (passengers == null || passengers.Count == 0)
                return 0.0;

            List<double> waits = null;

            if (designStepIndex >= 0)
                waits = CollectWaits(passengers, designStepIndex);

            // Mirrors QueueResults.GetEvaluationWait95: fall back to the
            // whole-run cohort when the design step contributes nobody.
            if (waits == null || waits.Count == 0)
                waits = CollectWaits(passengers, -1);

            if (waits.Count == 0)
                return 0.0;

            waits.Sort();

            return GetPercentile(waits, 0.95);
        }

        private static List<double> CollectWaits(List<Passenger> passengers, int stepIndex)
        {
            bool windowed = stepIndex >= 0;

            List<double> waits = new List<double>(passengers.Count);

            for (int i = 0; i < passengers.Count; i++)
            {
                Passenger passenger = passengers[i];

                if (passenger == null || passenger.IsWarmUp)
                    continue;

                if (windowed && passenger.DemandStepIndex != stepIndex)
                    continue;

                waits.Add(passenger.WaitTime);
            }

            return waits;
        }

        private void GetPassengerMetrics(
            List<Passenger> passengers,
            QueueResults results)
        {
            if (passengers == null || passengers.Count == 0)
                return;

            List<double> sortedWaitTimes =
                passengers
                .Select(p => p.WaitTime)
                .OrderBy(w => w)
                .ToList();

            results.AverageWait = passengers.Average(p => p.WaitTime);
            results.MaxWait = passengers.Max(p => p.WaitTime);
            results.Wait95 = GetPercentile(sortedWaitTimes, 0.95);
        }

        private static double GetPercentile(List<double> sortedValues, double percentile)
        {
            if (sortedValues == null || sortedValues.Count == 0)
                return 0.0;

            int index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
            index = Math.Max(0, Math.Min(sortedValues.Count - 1, index));

            return sortedValues[index];
        }

        /// <summary>
        /// Splits a cohort by a key (channel or profile) and computes the same
        /// statistics per group. Peak queue is the group's own contribution to
        /// the shared queue (a sweep-line over just that group).
        /// </summary>
        private static Dictionary<string, ClassMetrics> BuildGroupMetrics(
            List<Passenger> passengers,
            Func<Passenger, string> keySelector)
        {
            Dictionary<string, ClassMetrics> map =
                new Dictionary<string, ClassMetrics>(StringComparer.OrdinalIgnoreCase);

            if (passengers == null || passengers.Count == 0)
                return map;

            foreach (var group in passengers.GroupBy(keySelector))
            {
                List<Passenger> cohort = group.ToList();
                List<double> sortedWaitTimes =
                    cohort.Select(p => p.WaitTime).OrderBy(w => w).ToList();

                map[group.Key] = new ClassMetrics
                {
                    Key = group.Key,
                    Count = cohort.Count,
                    AverageWait = cohort.Average(p => p.WaitTime),
                    MaxWait = cohort.Max(p => p.WaitTime),
                    Wait95 = GetPercentile(sortedWaitTimes, 0.95),
                    PeakQueueLength = GroupPeakQueueLength(cohort)
                };
            }

            return map;
        }

        private static int GroupPeakQueueLength(List<Passenger> passengers)
        {
            List<(double Time, int Delta)> events = new List<(double, int)>();

            foreach (Passenger passenger in passengers)
            {
                if (passenger.StartServiceTime > passenger.ArrivalTime)
                {
                    events.Add((passenger.ArrivalTime, +1));
                    events.Add((passenger.StartServiceTime, -1));
                }
            }

            events.Sort((a, b) =>
            {
                int compare = a.Time.CompareTo(b.Time);
                return compare != 0 ? compare : a.Delta.CompareTo(b.Delta);
            });

            int queue = 0;
            int peak = 0;

            foreach (var e in events)
            {
                queue += e.Delta;
                if (queue > peak)
                    peak = queue;
            }

            return peak;
        }

        private List<QueueTimelinePoint> BuildQueueTimeline(List<Passenger> passengers)
        {
            List<QueueTimelinePoint> timeline = new List<QueueTimelinePoint>();

            if (passengers == null || passengers.Count == 0)
                return timeline;

            List<(double Time, int Delta)> events = new List<(double, int)>();

            foreach (Passenger passenger in passengers)
            {
                if (passenger.StartServiceTime > passenger.ArrivalTime)
                {
                    events.Add((passenger.ArrivalTime, +1));
                    events.Add((passenger.StartServiceTime, -1));
                }
            }

            events.Sort((a, b) =>
            {
                int compare = a.Time.CompareTo(b.Time);
                if (compare != 0)
                    return compare;

                return a.Delta.CompareTo(b.Delta);
            });

            int queue = 0;

            foreach (var e in events)
            {
                queue += e.Delta;
                timeline.Add(new QueueTimelinePoint { Time = e.Time, Queue = queue });
            }

            return timeline;
        }

        /// <summary>
        /// Upper bound on samples in <see cref="QueueResults.CumulativeFlow"/>.
        /// Above this the grid coarsens automatically: a minute grid over a very
        /// long horizon produces more points than anyone can read, and each
        /// extra sample costs a little plotting time.
        /// </summary>
        public const int MaxCumulativeSamples = 600;

        /// <summary>
        /// Cumulative arrivals against cumulative departures on a regular time
        /// grid, sampled over the same warm-up-free cohort as every other
        /// reported metric.
        ///
        /// Both arrival and departure times are sorted once and then read with a
        /// moving pointer. A naive "count every passenger for every sample" loop
        /// would be O(passengers x samples) - around eighteen million
        /// comparisons on the 30k-passenger case the test suite exercises, which
        /// is the difference between milliseconds and a visible stall.
        /// </summary>
        private static List<QueueCumulativePoint> BuildCumulativeFlow(
            List<Passenger> windowed)
        {
            List<QueueCumulativePoint> flow = new List<QueueCumulativePoint>();

            if (windowed == null || windowed.Count == 0)
                return flow;

            double start = double.MaxValue;
            double end = double.MinValue;

            List<double> arrivals = new List<double>(windowed.Count);
            List<double> departures = new List<double>(windowed.Count);

            for (int i = 0; i < windowed.Count; i++)
            {
                Passenger passenger = windowed[i];

                if (passenger == null)
                    continue;

                arrivals.Add(passenger.ArrivalTime);
                departures.Add(passenger.EndServiceTime);

                if (passenger.ArrivalTime < start)
                    start = passenger.ArrivalTime;

                if (passenger.EndServiceTime > end)
                    end = passenger.EndServiceTime;
            }

            if (arrivals.Count == 0)
                return flow;

            arrivals.Sort();
            departures.Sort();

            double duration = end - start;

            if (duration <= 0.0)
            {
                // Everyone arrived and was served at the same instant: a single
                // sample still describes the run.
                flow.Add(new QueueCumulativePoint(start, arrivals.Count, departures.Count));

                return flow;
            }

            double step = 1.0;

            if (duration / step > MaxCumulativeSamples)
                step = Math.Ceiling(duration / MaxCumulativeSamples);

            if (step < 1.0)
                step = 1.0;

            int arrived = 0;
            int departed = 0;

            for (double time = start; time <= end; time += step)
            {
                while (arrived < arrivals.Count && arrivals[arrived] <= time)
                    arrived++;

                while (departed < departures.Count && departures[departed] <= time)
                    departed++;

                flow.Add(new QueueCumulativePoint(time, arrived, departed));
            }

            // Always close the series on the final departure, otherwise the
            // diagram ends on a queue that never visibly drains.
            if (departed < departures.Count)
            {
                while (departed < departures.Count && departures[departed] <= end)
                    departed++;

                flow.Add(new QueueCumulativePoint(end, arrivals.Count, departed));
            }

            return flow;
        }

        private QueueTimelinePoint GetPeakPoint(List<QueueTimelinePoint> timeline)
        {
            QueueTimelinePoint peak = null;

            foreach (QueueTimelinePoint point in timeline)
            {
                if (peak == null || point.Queue > peak.Queue)
                    peak = point;
            }

            return peak;
        }

        private List<IntervalQueueMetrics> GetIntervalMetrics(
            List<Passenger> windowed,
            List<QueueTimelinePoint> timeline)
        {
            List<IntervalQueueMetrics> metrics = new List<IntervalQueueMetrics>();

            if (windowed == null || windowed.Count == 0)
                return metrics;

            var groups = windowed
                .Where(p => p.DemandStepIndex >= 0)
                .GroupBy(p => p.DemandStepIndex)
                .OrderBy(g => g.Key);

            foreach (var group in groups)
            {
                List<Passenger> cohort = group.ToList();

                List<double> sortedWaitTimes =
                    cohort.Select(p => p.WaitTime).OrderBy(w => w).ToList();

                double start = cohort.Min(p => p.ArrivalTime);
                double end = cohort.Max(p => p.EndServiceTime);

                IntervalQueueMetrics stepMetrics = new IntervalQueueMetrics
                {
                    StepIndex = group.Key,
                    StepStartMinute = start,
                    StepEndMinute = end,
                    ArrivalCount = cohort.Count,
                    ProcessedCount = cohort.Count,
                    AverageWait = cohort.Average(p => p.WaitTime),
                    MaxWait = cohort.Max(p => p.WaitTime),
                    Wait95 = GetPercentile(sortedWaitTimes, 0.95),
                    ByChannel = BuildGroupMetrics(
                        cohort, p => string.IsNullOrEmpty(p.Channel) ? "(none)" : p.Channel),
                    ByProfile = BuildGroupMetrics(
                        cohort, p => string.IsNullOrEmpty(p.ProfileName) ? "(unassigned)" : p.ProfileName)
                };

                QueueTimelinePoint stepPeak = GetPeakInRange(timeline, start, end);

                if (stepPeak != null)
                {
                    stepMetrics.PeakQueueLength = stepPeak.Queue;
                    stepMetrics.PeakQueueTime = stepPeak.Time;
                }

                double duration = end - start;

                stepMetrics.Throughput = duration > 0.0
                    ? cohort.Count / (duration / 60.0)
                    : 0.0;

                metrics.Add(stepMetrics);
            }

            return metrics;
        }

        private QueueTimelinePoint GetPeakInRange(
            List<QueueTimelinePoint> timeline,
            double start,
            double end)
        {
            QueueTimelinePoint peak = null;

            foreach (QueueTimelinePoint point in timeline)
            {
                if (point.Time < start || point.Time > end)
                    continue;

                if (peak == null || point.Queue > peak.Queue)
                    peak = point;
            }

            return peak;
        }

        private int FindStepAtTime(List<IntervalQueueMetrics> metrics, double time)
        {
            if (metrics == null)
                return -1;

            foreach (IntervalQueueMetrics metric in metrics)
            {
                if (time >= metric.StepStartMinute && time <= metric.StepEndMinute)
                    return metric.StepIndex;
            }

            return -1;
        }

        private List<double> GetCounterUtilization(
            List<Passenger> passengers,
            List<QueueResource> counters)
        {
            List<double> utilization = new List<double>();

            if (counters == null || counters.Count == 0)
                return utilization;

            if (passengers == null || passengers.Count == 0)
            {
                for (int i = 0; i < counters.Count; i++)
                    utilization.Add(0.0);

                return utilization;
            }

            double firstArrival = passengers.Min(p => p.ArrivalTime);
            double lastDeparture = passengers.Max(p => p.EndServiceTime);
            double simulationDuration = lastDeparture - firstArrival;

            if (simulationDuration <= 0.0)
            {
                for (int i = 0; i < counters.Count; i++)
                    utilization.Add(0.0);

                return utilization;
            }

            double[] busyTime = new double[counters.Count];

            foreach (Passenger passenger in passengers)
            {
                int id = passenger.AssignedCounter;

                if (id >= 0 && id < busyTime.Length)
                    busyTime[id] += passenger.ServiceTime;
            }

            for (int i = 0; i < counters.Count; i++)
            {
                double util = busyTime[i] / simulationDuration;
                utilization.Add(Math.Min(1.0, util));
            }

            return utilization;
        }

        public Dictionary<int, int> GetPassengersPerCounter(
            List<Passenger> passengers,
            List<QueueResource> counters)
        {
            Dictionary<int, int> passengersPerCounter =
                new Dictionary<int, int>();

            foreach (QueueResource counter in counters)
                passengersPerCounter[counter.Id] = 0;

            foreach (Passenger passenger in passengers)
            {
                if (passengersPerCounter.ContainsKey(passenger.AssignedCounter))
                    passengersPerCounter[passenger.AssignedCounter]++;
            }

            return passengersPerCounter;
        }
    }
}
