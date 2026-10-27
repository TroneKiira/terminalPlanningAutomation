using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Core.Queueing
{
    public class QueueSimulator
    {
        public void ResetPassengers(IEnumerable<Passenger> passengers)
        {
            if (passengers == null)
                throw new ArgumentNullException(nameof(passengers));

            foreach (Passenger passenger in passengers)
                passenger.ResetQueueState();
        }

        public void ResetResources(IEnumerable<QueueResource> resources)
        {
            if (resources == null)
                throw new ArgumentNullException(nameof(resources));

            foreach (QueueResource resource in resources)
                resource.Reset();
        }

        public void RunQueue(
            List<Passenger> passengers,
            List<QueueResource> resources,
            bool sortPassengers = true)
        {
            if (passengers == null)
                throw new ArgumentNullException(nameof(passengers));

            if (resources == null || resources.Count == 0)
                throw new ArgumentException("At least one queue resource is required.", nameof(resources));

            QueueResourceHeap heap = new QueueResourceHeap(resources);

            // Chronological processing. Callers that already hold a chronologically
            // ordered cohort (the optimizer reuses one pool across every candidate)
            // can skip this to avoid an O(n log n) pass per capacity trial.
            if (sortPassengers)
                passengers.Sort((a, b) => a.ArrivalTime.CompareTo(b.ArrivalTime));

            foreach (Passenger passenger in passengers)
            {
                if (passenger.ServiceTime < 0.0)
                {
                    throw new InvalidOperationException($"Passenger {passenger.Id} has a negative service time.");
                }

                QueueResource resource = heap.Pop();

                // Service may start before minute 0: arrivals use the schedule
                // clock, so early-morning flights correctly produce check-in
                // arrivals on the previous evening. Resources start available
                // before any simulated time, so a negative arrival is served at
                // its own arrival time rather than being held for midnight.
                passenger.StartServiceTime = Math.Max(passenger.ArrivalTime, resource.NextAvailableTime);
                passenger.WaitTime = passenger.StartServiceTime - passenger.ArrivalTime;
                passenger.EndServiceTime = passenger.StartServiceTime + passenger.ServiceTime;

                passenger.AssignedCounter = resource.Id;

                resource.NextAvailableTime = passenger.EndServiceTime;
                resource.BusyTime += passenger.ServiceTime;

                heap.Push(resource);
            }
        }
    }
}