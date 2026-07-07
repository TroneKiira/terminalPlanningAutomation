using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class Processor
    {
        public string Name { get; set; }
        public int Count { get; set; }
        public double Throughput { get; set; }
        public double Utilization { get; set; }
    }
}
