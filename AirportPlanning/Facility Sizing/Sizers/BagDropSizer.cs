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
    public class BagDropSizer : FacilitySizerBase
    {
        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards != null)
            {
                // Self-service bag drops are highly compact
                double areaPerUnit = Context.Standards.Width * Context.Standards.Depth;
                procZone.Area = Context.OperationalData.ProcessorCount * areaPerUnit;
                Result.EquipmentCounts["Self-Service Bag Drops"] = Context.OperationalData.ProcessorCount;
            }
        }
    }
}
