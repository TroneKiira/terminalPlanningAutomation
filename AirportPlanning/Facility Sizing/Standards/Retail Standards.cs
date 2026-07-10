using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Standards
{
    public class RetailStandards : FacilityStandards
    {
        // Space Benchmarks (sqm per peak passenger)
        public double BaseAreaPerDomesticPax { get; set; } = 0.8;
        public double BaseAreaPerInternationalPax { get; set; } = 1.5;

        // Commercial Mix Split (%)
        public double DutyFreePercentage { get; set; } = 35.0; // Usually only applies to International
        public double FnBPercentage { get; set; } = 40.0;
        public double SpecialtyPercentage { get; set; } = 15.0;
        public double ConveniencePercentage { get; set; } = 10.0;

        // Dwell Time Multiplier Baseline
        public double TargetDwellTime { get; set; } = 60.0; // Minutes
    }
}
