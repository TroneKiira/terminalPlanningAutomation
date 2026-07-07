using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Standards;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Rhino.UI.Controls.RenderContentMenu;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class CheckInSizer : FacilitySizerBase
    {
        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards != null)
            {
                double areaPerDesk = Context.Standards.Pitch * Context.Standards.Depth;
                procZone.Area = Context.OperationalData.ProcessorCount * areaPerDesk;
            }
        }

        protected override void CalculateSupportArea()
        {
            var supportZone = GetZone(ZoneType.Support);
            // FIX: Cast to CheckInStandards to use the proper formula variable
            if (supportZone != null && Context.Standards is CheckInStandards ciStd)
            {
                double rearWorkingArea = Context.OperationalData.ProcessorCount * (ciStd.Pitch * ciStd.RearWorkingSpace);
                supportZone.Area = rearWorkingArea;
            }
        }

        protected override FacilitySizingResult BuildResult()
        {
            var result = base.BuildResult();
            if (Context.OperationalData.ProcessorCount > 20)
            {
                result.PreferredTopology = "Island";
                result.PlanningAdvice.Add(new PlanningAdvice("Topology Changed", "More than 20 desks required. Switched to Island.", "Warning"));
            }
            return result;
        }
    }

}
