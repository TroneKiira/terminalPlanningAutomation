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

        // CISF Specifics
        public bool IsGenderSegregated { get; set; } = true;
        public double XRayThroughput { get; set; } = 250.0;     // Pax per hour per X-Ray
        public double FriskingThroughput { get; set; } = 150.0; // Pax per hour per WTMD/Booth
        public double FriskingBoothWidth { get; set; } = 1.2;
        public double FriskingBoothDepth { get; set; } = 1.5;
    }
}
