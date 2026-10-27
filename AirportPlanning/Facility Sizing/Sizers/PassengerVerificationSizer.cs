using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class PassengerVerificationSizer : FacilitySizerBase
    {
        protected override void CalculateQueueArea()
        {
            var queueZone = GetZone(ZoneType.Queue);
            if (queueZone != null) queueZone.Area = 0.0;
        }

        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards != null)
            {
                // 🚨 REVERSE MATH: If the user overrode the gates, recalculate the Utilization!
                if (Context.OperationalData.IsOverridden)
                {
                    double demand = Context.PassengerSummary.TotalPassengers;
                    double rawThroughput = Context.OperationalData.Throughput; // e.g., 300 pax/hr
                    int manualGates = Context.OperationalData.ProcessorCount;

                    if (manualGates > 0 && rawThroughput > 0)
                    {
                        // New Util = Demand / (Gates * Max Throughput)
                        Context.OperationalData.Utilization = demand / (manualGates * rawThroughput);
                    }
                }

                // Calculate the physical area
                procZone.Area = Context.OperationalData.ProcessorCount * (Context.Standards.Width * Context.Standards.Depth);

                // 🚨 OUTPUT THE EQUIPMENT COUNTS
                Result.EquipmentCounts["Physical E-Gates"] = Context.OperationalData.ProcessorCount;
            }
        }
    }
}
