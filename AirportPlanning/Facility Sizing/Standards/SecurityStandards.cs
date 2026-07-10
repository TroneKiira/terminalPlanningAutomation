using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Standards
{
    public class SecurityStandards : FacilityStandards
    {
        public double DivestRecomposeAllowance { get; set; } = 10.0;

        // CISF / Indian Security Specifics
        public bool IsGenderSegregated { get; set; } = true;

        // FIX: Set these to match the 2.2 ratio of your reference plan!
        public double XRayThroughput { get; set; } = 330.0;     // Pax per hour per X-Ray
        public double FriskingThroughput { get; set; } = 150.0; // Pax per hour per Booth

        public double MaleToFemaleRatio { get; set; } = 0.70;   // 70% Male, 30% Female
        public double FriskingBoothWidth { get; set; } = 1.2;
        public double FriskingBoothDepth { get; set; } = 1.5;
    }
}
