using System.Collections.Generic;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class FacilitySizingResult
    {
        public int PeakDemand { get; set; }
        public int ProcessorCount { get; set; }

        // 🚨 NEW: Carry the baseline so the Reporter knows if the user overrode it!
        public int BaselineProcessorCount { get; set; }

        public double QueueArea { get; set; }
        public double ProcessingArea { get; set; }
        public double SupportArea { get; set; }
        public double CirculationArea { get; set; }
        public double TotalArea { get; set; }

        /// <summary>
        /// Queue, processing, support and fixed areas before any allowance,
        /// m². This is the area the allowances are applied to.
        /// </summary>
        public double NetArea { get; set; }

        /// <summary>Mechanical, electrical and plumbing allowance, m².</summary>
        public double MepArea { get; set; }

        /// <summary>Structure allowance, m².</summary>
        public double StructureArea { get; set; }

        /// <summary>
        /// Total buildable area, m²: net, then circulation, then MEP, then
        /// structure. The same figure as <see cref="TotalArea"/>.
        /// </summary>
        public double GrossArea { get; set; }

        /// <summary>
        /// One line per routed pool, showing the units, the standard and the
        /// area each contributed.
        /// </summary>
        public List<string> PoolAreas { get; set; } = new List<string>();

        /// <summary>The area build-up as readable lines, net to gross.</summary>
        public List<string> AreaLines { get; set; } = new List<string>();

        /// <summary>Anything the reader needs to know about the result.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        public string PreferredTopology { get; set; }
        public PreferredAxis PreferredAxis { get; set; }

        public List<PlanningAdvice> PlanningAdvice { get; set; } = new List<PlanningAdvice>();
        public Dictionary<string, int> EquipmentCounts { get; set; } = new Dictionary<string, int>();
    }
}
