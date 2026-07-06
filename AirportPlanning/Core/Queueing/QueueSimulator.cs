using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

using Rhino;
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

        public void RunQueue(List<Passenger> passengers, List<QueueResource> resources)
        {
            if (passengers == null)
                throw new ArgumentNullException(nameof(passengers));

            if (resources == null || resources.Count == 0)
                throw new ArgumentException("At least one queue resource is required.", nameof(resources));

            QueueResourceHeap heap = new QueueResourceHeap(resources);

            // FIX: Absolute safeguard to ensure chronological processing
            passengers.Sort((a, b) => a.ArrivalTime.CompareTo(b.ArrivalTime));

            foreach (Passenger passenger in passengers)
            {
                if (passenger.ServiceTime < 0.0)
                {
                    throw new InvalidOperationException($"Passenger {passenger.Id} has a negative service time.");
                }

                QueueResource resource = heap.Pop();

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