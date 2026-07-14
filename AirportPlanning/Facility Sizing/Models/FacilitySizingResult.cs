using System.Collections.Generic;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class FacilitySizingResult
    {
        // 🚨 THE MISSING PROPERTY
        public int PeakDemand { get; set; }

        // Ensure ProcessorCount is here too!
        public int ProcessorCount { get; set; }

        public double QueueArea { get; set; }
        public double ProcessingArea { get; set; }
        public double SupportArea { get; set; }
        public double CirculationArea { get; set; }
        public double TotalArea { get; set; }

        public string PreferredTopology { get; set; }
        public PreferredAxis PreferredAxis { get; set; }

        public List<PlanningAdvice> PlanningAdvice { get; set; } = new List<PlanningAdvice>();
        public Dictionary<string, int> EquipmentCounts { get; set; } = new Dictionary<string, int>();
    }
}