using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Standards
{
    public class BagDropStandards : FacilityStandards
    {
        // Specific to Bag Drop (from Section 9)
        public double ConveyorWidth { get; set; }
        public double ConveyorDepth { get; set; }
    }
}
