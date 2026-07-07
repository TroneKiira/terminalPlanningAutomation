using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class FacilitySizingResult
    {
        // Operational Metrics
        public int PeakQueue { get; set; }
        public double AverageWait { get; set; }
        public double Wait95 { get; set; }
        public double Utilization { get; set; }

        // Spatial Areas
        public double QueueArea { get; set; }
        public double ProcessingArea { get; set; }
        public double CirculationArea { get; set; }
        public double StaffArea { get; set; }       
        public double EquipmentArea { get; set; }   
        public double SupportArea { get; set; }
        public double TotalArea { get; set; }

        // Geometric Layout Recommendations
        public double RecommendedWidth { get; set; }
        public double RecommendedDepth { get; set; }
        public int ProcessorCount { get; set; }
        public double RecommendedQueueWidth { get; set; } 
        public double QueueLength { get; set; }           

        // Architectural
        public string PreferredTopology { get; set; }
        public PreferredAxis PreferredAxis { get; set; }

        // Planning Validation
        public string TargetLOS { get; set; }   
        public string AchievedLOS { get; set; } 
        public List<PlanningAdvice> PlanningAdvice { get; set; } = new List<PlanningAdvice>();

        // NEW: Store the exact equipment counts as raw integers!
        public Dictionary<string, int> EquipmentCounts { get; set; } = new Dictionary<string, int>();
    }
}
