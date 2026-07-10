using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Standards
{
    public class BoardingStandards : FacilityStandards
    {
        // Macro Inputs (Gate Count)
        public int CriticalAircraftCapacity { get; set; } = 180;
        public double PassengerLoadFactor { get; set; } = 0.85;
        public double TurnaroundTime { get; set; } = 60.0; // Minutes
        public double GateUtilization { get; set; } = 0.80;

        // Micro Inputs (Hold Room Sizing)
        public double GateShowUpRate { get; set; } = 0.85;
        public double SeatingProvision { get; set; } = 0.60;

        // IATA Spatial Standards (from your PDF)
        public double SeatedAreaPerPax { get; set; } = 2.00;   // IATA: 1.8 - 2.2 sqm
        public double StandingAreaPerPax { get; set; } = 1.35; // IATA: 1.2 - 1.5 sqm
    }
}
