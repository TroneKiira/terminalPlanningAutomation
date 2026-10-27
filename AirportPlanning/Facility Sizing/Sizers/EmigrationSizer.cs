using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class EmigrationSizer : FacilitySizerBase
    {
        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards != null)
            {
                double pairedArea = Context.Standards.Width * Context.Standards.Depth;

                // 🚨 Uses ProcessorCount (25) to calculate the physical building area
                int physicalBooths = (Context.OperationalData.ProcessorCount + 1) / 2;
                procZone.Area = physicalBooths * pairedArea;

                Result.EquipmentCounts["Emigration Positions"] = physicalBooths;

                // 🚨 Uses ActiveProcessorCount (15) for the text readout
                Result.EquipmentCounts["Active Agent Positions"] = Context.OperationalData.ActiveProcessorCount;
            }
        }

        

        protected override FacilitySizingResult BuildResult()
        {
            var result = base.BuildResult();

            // Smart Rule: E-Gate suggestion
            if (Context.OperationalData.ProcessorCount > 15)
            {
                result.PlanningAdvice.Add(new PlanningAdvice(
                    "Automation Recommended",
                    "High processor count detected. Consider converting 30% of manual booths to E-Gates to save spatial footprint.",
                    "Warning"));
            }

            return result;
        }
    }
}
