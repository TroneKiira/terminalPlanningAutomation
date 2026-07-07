using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Standards;
using AirportPlanning.Facility_Sizing.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Base
{
    public class FacilitySizingContext
    {
        public FacilityOperationalData OperationalData { get; set; }
        public PassengerSummaryData PassengerSummary { get; set; }
        public FacilityStandards Standards { get; set; }
        public FacilityTemplate Template { get; set; }
        public PlanningSettings Settings { get; set; }
    }
}
