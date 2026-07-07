using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Standards
{
    public class CheckInStandards : FacilityStandards
    {
        public double RearWorkingSpace { get; set; } = 2.5; // Default 2.5m
        public double ConveyorWidth { get; set; }
        public double ConveyorDepth { get; set; }
    }
}
