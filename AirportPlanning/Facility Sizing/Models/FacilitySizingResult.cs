using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class FacilitySizingResult
    {
        public double QueueArea { get; set; }
        public double ProcessingArea { get; set; }
        public double SupportArea { get; set; }
        public double CirculationArea { get; set; }
        public double TotalArea { get; set; }

        public string PreferredTopology { get; set; }
        public PreferredAxis PreferredAxis { get; set; }

        public List<PlanningAdvice> PlanningAdvice { get; set; } = new List<PlanningAdvice>();
    }
}
