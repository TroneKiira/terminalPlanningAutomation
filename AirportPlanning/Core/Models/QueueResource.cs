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
        /// </summary>
        public double NextAvailableTime { get; set; }

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
            NextAvailableTime = 0.0;
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
