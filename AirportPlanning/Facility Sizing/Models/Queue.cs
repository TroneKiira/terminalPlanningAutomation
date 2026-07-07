using AirportPlanning.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class Queue
    {
        // Operational stats carried over
        public int PeakQueue { get; set; }
        public double Wait95 { get; set; }
        public double AverageWait { get; set; }
        public double MaximumWait { get; set; }

        // Architectural addition
        public double QueueArea { get; set; }

        public Queue(QueueResults results)
        {
            if (results != null)
            {
                PeakQueue = results.PeakQueueLength;
                Wait95 = results.Wait95;
                AverageWait = results.AverageWait;
                MaximumWait = results.MaxWait;
            }
        }
    }
}
