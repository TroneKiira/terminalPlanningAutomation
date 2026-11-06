using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Schedules
{
    /// <summary>Arrival peak-hour demand derived from a parsed schedule.</summary>
    public class ScheduleArrivalPeakResult
    {
        /// <summary>
        /// Passengers in the busiest rolling 60-minute window.
        /// 0 when no arrival leg contributes.
        /// </summary>
        public int PeakPassengers { get; set; }

        /// <summary>
        /// Minute of the day where the peak window starts. -1 when no
        /// arrival leg contributes.
        /// </summary>
        public int PeakWindowStartMinute { get; set; }

        /// <summary>Arrival passenger legs that contributed a count.</summary>
        public int ArrivalLegsUsed { get; set; }

        /// <summary>
        /// Arrival passenger legs that could not contribute: no usable time,
        /// or no passenger count and no seats to fall back to.
        /// </summary>
        public int ArrivalLegsExcluded { get; set; }
    }

    /// <summary>
    /// Derives the arrival peak-hour passenger count (PHP) from a parsed
    /// flight schedule: passenger arrival legs are binned into 5-minute
    /// buckets on the continuous schedule clock and summed over every
    /// rolling 60-minute window. This is the schedule-derived value for the
    /// Deplaning Profile's PHP input.
    /// </summary>
    public static class ScheduleArrivalPeakCalculator
    {
        private const int BucketMinutes = 5;
        private const int WindowBuckets = 60 / BucketMinutes;

        public static ScheduleArrivalPeakResult Calculate(
            FlightScheduleResult schedule)
        {
            if (schedule == null || schedule.Legs == null)
                return new ScheduleArrivalPeakResult
                {
                    PeakWindowStartMinute = -1
                };

            return Calculate(schedule.Legs);
        }

        public static ScheduleArrivalPeakResult Calculate(
            IEnumerable<FlightLeg> legs)
        {
            ScheduleArrivalPeakResult result = new ScheduleArrivalPeakResult();

            if (legs == null)
            {
                result.PeakWindowStartMinute = -1;
                return result;
            }

            Dictionary<int, int> buckets = new Dictionary<int, int>();

            foreach (FlightLeg leg in legs)
            {
                if (leg == null)
                    continue;

                // PHP is an arrival passenger count: freight and other
                // service legs never carry passengers and are simply not
                // relevant, so they are neither used nor counted as excluded.
                if (leg.Direction != LegDirection.Arrival ||
                    leg.ServiceType != LegServiceType.Passenger)
                    continue;

                if (!leg.HasTime)
                {
                    result.ArrivalLegsExcluded++;
                    continue;
                }

                int? pax = ResolvePassengerCount(leg);

                if (!pax.HasValue)
                {
                    result.ArrivalLegsExcluded++;
                    continue;
                }

                result.ArrivalLegsUsed++;

                if (pax.Value <= 0)
                    continue;

                int bucket = leg.TotalMinutesFromMidnight / BucketMinutes;
                buckets[bucket] = GetOrZero(buckets, bucket) + pax.Value;
            }

            if (buckets.Count == 0)
            {
                result.PeakWindowStartMinute = -1;
                return result;
            }

            // Windows are anchored at every bucket that holds demand; windows
            // extending past the last leg simply sum against zero buckets and
            // cannot become the peak unless the tail genuinely is the peak.
            int minBucket = buckets.Keys.Min();
            int maxBucket = buckets.Keys.Max();
            int peak = 0;
            int peakAnchor = minBucket;

            for (int anchor = minBucket; anchor <= maxBucket; anchor++)
            {
                int sum = 0;

                for (int offset = 0; offset < WindowBuckets; offset++)
                    sum += GetOrZero(buckets, anchor + offset);

                if (sum > peak)
                {
                    peak = sum;
                    peakAnchor = anchor;
                }
            }

            result.PeakPassengers = peak;
            result.PeakWindowStartMinute =
                (peakAnchor * BucketMinutes) % 1440;

            return result;
        }

        /// <summary>
        /// Passenger count for one leg: the file's PAX column when present,
        /// otherwise seats x load factor (a missing load factor is read as a
        /// full flight). Null when neither source is usable.
        /// </summary>
        private static int? ResolvePassengerCount(FlightLeg leg)
        {
            if (leg.PaxRaw.HasValue)
                return leg.PaxRaw.Value < 0 ? (int?)null : leg.PaxRaw.Value;

            if (!leg.SeatsRaw.HasValue || leg.SeatsRaw.Value <= 0)
                return null;

            double loadFactor = leg.LoadFactor ?? 1.0;

            if (loadFactor < 0.0)
                loadFactor = 1.0;

            if (loadFactor > 1.0)
                loadFactor = 1.0;

            return (int)Math.Round(leg.SeatsRaw.Value * loadFactor);
        }

        private static int GetOrZero(
            Dictionary<int, int> buckets,
            int bucket)
        {
            int value;
            return buckets.TryGetValue(bucket, out value) ? value : 0;
        }
    }
}
