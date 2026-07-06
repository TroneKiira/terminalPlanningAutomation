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

            GetPassengerMetrics(
                passengers,
                results);

            results.PeakQueueLength =
                GetPeakQueueLength(passengers);

            results.CounterUtilization =
                GetCounterUtilization(
                    passengers,
                    counters);

            return results;
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

            int index =
                (int)Math.Ceiling(0.95 * sortedWaitTimes.Count) - 1;

            index = Math.Max(0, index);

            results.AverageWait =
                passengers.Average(p => p.WaitTime);

            results.MaxWait =
                passengers.Max(p => p.WaitTime);

            results.Wait95 =
                sortedWaitTimes[index];
        }

        private int GetPeakQueueLength(List<Passenger> passengers)
        {
            if (passengers == null || passengers.Count == 0)
                return 0;

            List<(double Time, int Delta)> events = new List<(double, int)>();

            foreach (Passenger passenger in passengers)
            {
                // NEW: Only add to queue events if they actually had to wait
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
            int peak = 0;

            foreach (var e in events)
            {
                queue += e.Delta;
                if (queue > peak)
                    peak = queue;
            }

            return peak;
        }
        private List<double> GetCounterUtilization(
            List<Passenger> passengers,
            List<QueueResource> counters)
        {
            List<double> utilization =
                new List<double>();

            if (counters == null || counters.Count == 0)
                return utilization;

            if (passengers == null || passengers.Count == 0)
            {
                for (int i = 0; i < counters.Count; i++)
                    utilization.Add(0.0);

                return utilization;
            }

            double simulationDuration =
                passengers.Max(p => p.EndServiceTime);

            if (simulationDuration <= 0.0)
            {
                for (int i = 0; i < counters.Count; i++)
                    utilization.Add(0.0);

                return utilization;
            }

            foreach (QueueResource counter in counters)
            {
                utilization.Add(
                    counter.BusyTime /
                    simulationDuration);
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
