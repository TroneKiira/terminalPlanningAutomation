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

        public int ResourcesUsed { get; set; }

        public double SimulationDuration { get; set; }

        public int PeakQueueLength { get; set; }

        public double AverageWait { get; set; }

        public double MaxWait { get; set; }

        public double Wait95 { get; set; }

        public List<double> CounterUtilization { get; set; }

        public int BaselineProcessorCount { get; set; } = -1;

        public int PhysicalProcessorCount { get; set; } = -1;

        public QueueResults()
        {
            Passengers =
                new List<Passenger>();

            ProcessedPassengers =
                new List<Passenger>();

            Resources =
                new List<QueueResource>();

            CounterUtilization =
                new List<double>();
        }
    }
}
