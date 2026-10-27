using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    /// <summary>
    /// Terminal entry is a position-based facility: each installed manual
    /// position or e-gate has a footprint, and the waiting area follows the
    /// queue peak. It can also be sized from routed pool standards, so manual
    /// and DigiYatra positions may use their own unit areas.
    /// </summary>
    public class TerminalEntrySizer : FacilitySizerBase
    {
        protected override bool SupportsRoutedPoolAreas
        {
            get { return true; }
        }

        protected override void CalculateProcessingArea()
        {
            base.CalculateProcessingArea();

            if (Context.Standards != null)
            {
                int physical = Context.OperationalData.ProcessorCount;
                Result.EquipmentCounts["Terminal Entry Positions"] = physical;
                Result.EquipmentCounts["Active Terminal Entry Positions"] =
                    Context.OperationalData.ActiveProcessorCount;
            }
        }
    }
}
