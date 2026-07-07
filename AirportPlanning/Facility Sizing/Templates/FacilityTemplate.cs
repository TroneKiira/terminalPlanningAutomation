using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Templates
{
    public abstract class FacilityTemplate
    {
        // NEW: A list of valid topologies for this specific facility
        public List<string> AvailableTopologies { get; set; } = new List<string>();

        public string PreferredTopology { get; set; }
        public PreferredAxis PreferredAxis { get; set; }
        public double PreferredAspectRatio { get; set; }
        public Relationship Relationship { get; set; }
        public List<PlanningAdvice> PlanningAdvice { get; set; }

        protected FacilityTemplate()
        {
            PlanningAdvice = new List<PlanningAdvice>();
        }
    }
}
