using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Models
{
    public class QueueResource
    {
        public int Id { get; set; }

        /// <summary>
        /// Earliest time this resource becomes available.
        ///
        /// A fresh resource is available before any simulated time, not at
        /// time 0. Minute 0 is just a point on the simulation clock - midnight
        /// for schedule-based runs - not a facility opening time. Pinning this
        /// to 0 would make every passenger who arrives before midnight wait for
        /// midnight, producing a fake queue that collapses at 0 instead of the
        /// real queue building and draining across the boundary.
        /// </summary>
        public double NextAvailableTime { get; set; } = double.NegativeInfinity;

        /// <summary>
        /// Total time this resource spent serving passengers.
        /// Used for utilization calculations.
        /// </summary>
        public double BusyTime { get; set; }

        /// <summary>
        /// Resets the resource before a new queue simulation.
        /// </summary>
        public void Reset()
        {
            NextAvailableTime = double.NegativeInfinity;
            BusyTime = 0.0;
        }

        public static List<QueueResource> Create(
            int numberOfResources)
        {
            List<QueueResource> resources =
                new List<QueueResource>();

            for (int i = 0; i < numberOfResources; i++)
            {
                QueueResource resource =
                    new QueueResource();

                resource.Id = i;

                resources.Add(resource);
            }

            return resources;
        }
    }

}
