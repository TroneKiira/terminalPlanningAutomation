using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class FacilityOperationalData
    {
        public int ProcessorCount { get; set; }
        public int PeakQueue { get; set; }
        public double AverageQueue { get; set; }
        public double AverageWait { get; set; }
        public double MaximumWait { get; set; }
        public double Wait95 { get; set; }
        public double Utilization { get; set; }
        public double Throughput { get; set; }
        public int PeakOccupancy { get; set; }
        public AnalysisMethod AnalysisMethod { get; set; }
    }
}
