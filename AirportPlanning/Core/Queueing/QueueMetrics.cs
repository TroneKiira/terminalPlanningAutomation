using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Core.Queueing
{
    public class QueueMetrics
    {
        public double AverageWait { get; set; }

        public double MaxWait { get; set; }

        public double Wait95 { get; set; }

        public int PeakQueueLength { get; set; }

        public List<double> CounterUtilization { get; set; }

        public QueueMetrics()
        {
            CounterUtilization = new List<double>();
        }
    }
}
