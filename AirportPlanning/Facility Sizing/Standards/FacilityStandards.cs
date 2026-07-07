using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Standards
{
    public class FacilityStandards
    {
        // Operational
        public double ProcessingTime { get; set; }
        public double TargetUtilization { get; set; }
        public double MaximumWait { get; set; }

        // Queue
        public double QueueAreaPerPassenger { get; set; }
        public double QueueWidth { get; set; }

        // Processor
        public double Width { get; set; }
        public double Depth { get; set; }
        public double Pitch { get; set; }

        // Equipment
        public double EquipmentWidth { get; set; }
        public double EquipmentDepth { get; set; }
        public double EquipmentClearance { get; set; }

        // Planning
        public double CirculationPercent { get; set; }
        public double SupportPercent { get; set; }
        public string LevelOfService { get; set; }
    }
}
