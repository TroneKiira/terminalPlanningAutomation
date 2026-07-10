using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Rhino.UI.Controls.RenderContentMenu;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class ImmigrationSizer : FacilitySizerBase
    {
        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards != null)
            {
                // Immigration booths are usually paired (2 agents per physical footprint)
                double pairedArea = Context.Standards.Width * Context.Standards.Depth;
                int physicalBooths = (Context.OperationalData.ProcessorCount + 1) / 2;
                procZone.Area = physicalBooths * pairedArea;
                Result.EquipmentCounts["Immigration Booths"] = physicalBooths;
                Result.EquipmentCounts["Active Agent Positions"] = Context.OperationalData.ProcessorCount;
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
