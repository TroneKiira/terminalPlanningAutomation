using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Standards
{
    public class PlanningSettings
    {
        public string TargetLevelOfService { get; set; }
        public double ExpansionFactor { get; set; }
        public DesignMode DesignMode { get; set; }
    }
}
