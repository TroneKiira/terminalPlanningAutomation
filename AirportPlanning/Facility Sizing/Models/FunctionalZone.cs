using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Models
{
    public class FunctionalZone
    {
        public ZoneType ZoneType { get; set; }
        public double Area { get; set; }

        public FunctionalZone(ZoneType type)
        {
            ZoneType = type;
            Area = 0.0;
        }
    }
}
