using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class BagDropSizer : FacilitySizerBase
    {
        /// <summary>
        /// A bag-drop hall is the units it has and the queue in front of
        /// them, so it can be sized from routed pool standards.
        /// </summary>
        protected override bool SupportsRoutedPoolAreas
        {
            get { return true; }
        }

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