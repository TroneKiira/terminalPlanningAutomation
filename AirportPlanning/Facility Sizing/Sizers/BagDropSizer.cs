using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class BagDropSizer : FacilitySizerBase
    {
        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards != null)
            {
                double areaPerUnit = Context.Standards.Width * Context.Standards.Depth;
                procZone.Area = Context.OperationalData.ProcessorCount * areaPerUnit;

                // 🚨 Explicitly output both physical and active counts!
                Result.EquipmentCounts["Physical SBD Units"] = Context.OperationalData.ProcessorCount;
                Result.EquipmentCounts["Active SBD Units"] = Context.OperationalData.ActiveProcessorCount;
            }
        }
    }
}