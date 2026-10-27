using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Core.Charts
{
    /// <summary>
    /// Helpers for the audit outputs every chart publishes next to its bitmap:
    /// a readable summary and a CSV table of the values actually plotted.
    ///
    /// Keeping the row format in one place means every chart can be exported,
    /// diffed and cross-checked the same way.
    /// </summary>
    public static class ChartText
    {
        /// <summary>Invariant numeric formatting used in CSV rows.</summary>
        public static string Number(double value)
        {
            if (double.IsNaN(value))
                return "NaN";

            if (double.IsInfinity(value))
                return value > 0 ? "Infinity" : "-Infinity";

            // Collapse floating-point dust and negative zero. A queue event at
            // -1e-15 is at minute 0, and "-0" reads as a real negative timestamp.
            if (Math.Abs(value) < 1e-9)
                return "0";

            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Joins cells into one CSV row, quoting cells that need it.</summary>
        public static string Row(params string[] cells)
        {
            if (cells == null)
                return string.Empty;

            return string.Join(",", cells.Select(Escape));
        }

        /// <summary>
        /// Escapes a CSV cell. Numeric and plain identities pass through
        /// unchanged; commas, quotes and newlines are quoted.
        /// </summary>
        public static string Escape(string value)
        {
            if (value == null)
                return string.Empty;

            bool needsQuotes =
                value.IndexOf(',') >= 0 ||
                value.IndexOf('"') >= 0 ||
                value.IndexOf('\n') >= 0 ||
                value.IndexOf('\r') >= 0;

            if (!needsQuotes)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>
        /// Selects the pools a chart should show. An empty filter means "use the
        /// facility aggregate"; "*" or "all" means every recorded pool. Any
        /// other value is a comma/semicolon-separated list matched against the
        /// full pool identity, its channel, or as a channel prefix, so
        /// "Counter" selects every Counter sub-pool.
        /// </summary>
        public static List<QueuePoolResult> SelectPools(
            IEnumerable<QueuePoolResult> pools,
            string filter)
        {
            List<QueuePoolResult> all = pools == null
                ? new List<QueuePoolResult>()
                : pools.Where(p => p != null).ToList();

            if (all.Count == 0 || string.IsNullOrWhiteSpace(filter))
                return new List<QueuePoolResult>();

            string trimmed = filter.Trim();

            if (trimmed == "*" ||
                string.Equals(trimmed, "all", StringComparison.OrdinalIgnoreCase))
            {
                return all;
            }

            List<string> tokens = trimmed
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            List<QueuePoolResult> selected = new List<QueuePoolResult>();

            foreach (QueuePoolResult pool in all)
            {
                foreach (string token in tokens)
                {
                    if (IsMatch(pool, token))
                    {
                        selected.Add(pool);
                        break;
                    }
                }
            }

            return selected;
        }

        private static bool IsMatch(QueuePoolResult pool, string token)
        {
            if (string.Equals(pool.Name, token, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(pool.Channel, token, StringComparison.OrdinalIgnoreCase))
                return true;

            string prefix = token + "|";

            return pool.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Human-readable target status for one pool.
        /// </summary>
        public static string TargetStatus(QueuePoolResult pool)
        {
            if (pool == null)
                return "n/a";

            if (!pool.TargetApplied)
                return "no target";

            return pool.TargetMet
                ? $"met (W95 {Number(pool.Wait95)} <= {Number(pool.TargetWait95)})"
                : $"missed (W95 {Number(pool.Wait95)} > {Number(pool.TargetWait95)})";
        }

        /// <summary>
        /// Arrival and queue activity for one pool: when its passengers arrived,
        /// when they actually queued, and how many waited.
        ///
        /// A flat queue line after some minute is otherwise ambiguous. With this
        /// line it reads either "no passenger waited" or "the queue stopped at
        /// minute X", so the chart can be checked against the demand that fed it.
        /// </summary>
        public static string DescribeActivity(QueuePoolResult pool)
        {
            if (pool == null)
                return "n/a";

            if (pool.PassengerCount <= 0)
                return "no passengers";

            if (double.IsNaN(pool.FirstArrivalTime) ||
                double.IsNaN(pool.LastArrivalTime))
            {
                // Results produced before the activity span was recorded still
                // have a queue history but no arrival range to describe it.
                return "arrival span not recorded; re-run the facility solver";
            }

            string arrivals =
                $"{Number(pool.FirstArrivalTime)} to {Number(pool.LastArrivalTime)} min";

            if (pool.WaitingPassengerCount <= 0 ||
                double.IsNaN(pool.FirstQueueEventTime))
            {
                return $"arrivals {arrivals}; no passenger waited";
            }

            return string.Format(
                "arrivals {0}; {1}/{2} pax waited; queue events {3} to {4} min",
                arrivals,
                pool.WaitingPassengerCount,
                pool.PassengerCount,
                Number(pool.FirstQueueEventTime),
                Number(pool.LastQueueEventTime));
        }

        /// <summary>
        /// Arrival and queue activity for a facility aggregate. Uses the same
        /// warm-up-free population as the reported metrics.
        /// </summary>
        public static string DescribeActivity(
            IEnumerable<Passenger> passengers,
            IEnumerable<QueueTimelinePoint> timeline)
        {
            List<Passenger> served = passengers == null
                ? new List<Passenger>()
                : passengers.Where(p => p != null && !p.IsWarmUp).ToList();

            if (served.Count == 0)
                return "no passengers";

            List<QueueTimelinePoint> events = timeline == null
                ? new List<QueueTimelinePoint>()
                : timeline.Where(p => p != null).OrderBy(p => p.Time).ToList();

            string arrivals =
                $"{Number(served.Min(p => p.ArrivalTime))} to {Number(served.Max(p => p.ArrivalTime))} min";

            int waiting = served.Count(p => p.WaitTime > 1e-9);

            if (waiting == 0 || events.Count == 0)
                return $"arrivals {arrivals}; no passenger waited";

            return string.Format(
                "arrivals {0}; {1}/{2} pax waited; queue events {3} to {4} min",
                arrivals,
                waiting,
                served.Count,
                Number(events[0].Time),
                Number(events[events.Count - 1].Time));
        }
    }
}
