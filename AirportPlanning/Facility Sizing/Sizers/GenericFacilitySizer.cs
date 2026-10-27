using AirportPlanning.Core.Area;
using AirportPlanning.Core.Models;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    /// <summary>
    /// Applies the same area-per-installed-unit method to every facility.
    /// Counts come from the facility solver; queue peaks and pool identities
    /// come from Queue Results; area assumptions come from A-STD.
    /// </summary>
    public class GenericFacilitySizer : FacilitySizerBase
    {
        protected override void CalculateAreas()
        {
            FacilityAreaStandardSet standards = Context.AreaStandards;

            if (standards == null)
            {
                Result.Warnings.Add("Facility Area Standards (A-STD) are required to calculate area.");
                return;
            }

            FacilityAreaResult area = FacilityAreaCalculator.Calculate(new FacilityAreaInput
            {
                PoolNames = Context.PoolNames ?? new List<string>(),
                PhysicalPerPool = Context.PhysicalPerPool ?? new List<int>(),
                ActivePerPool = Context.ActivePerPool ?? new List<int>(),
                Pools = Context.ProcessorPools ?? new List<ProcessorPoolInfo>(),
                PeakQueueByChannel = Context.PeakQueueByChannel ??
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                PeakQueueByPool = Context.PeakQueueByPool ??
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                GlobalPeakQueue = Context.GlobalPeakQueue,
                Standards = standards,
                RequireExplicitCounts = true
            });

            SetZone(ZoneType.Queue, area.QueueArea);
            SetZone(ZoneType.Processing, area.EquipmentArea);

            double supportArea = area.EquipmentArea * Math.Max(0.0, standards.SupportPercent) / 100.0;
            SetZone(ZoneType.Support, supportArea);

            // Keep the result totals aligned with the solver counts actually
            // matched to QR pools (extra list entries are reported and ignored).
            Context.OperationalData.ProcessorCount = area.Lines.Sum(line => line.PhysicalUnits);
            Context.OperationalData.ActiveProcessorCount = area.Lines.Sum(line => line.ActiveUnits);

            Result.PoolAreas.AddRange(area.Describe());
            Result.Warnings.AddRange(area.Warnings);

            foreach (FacilityAreaLine line in area.Lines)
            {
                string poolName = string.IsNullOrWhiteSpace(line.PoolName)
                    ? "Unnamed pool"
                    : line.PoolName;

                Result.EquipmentCounts[poolName + " units (installed)"] = line.PhysicalUnits;
                Result.EquipmentCounts[poolName + " units (staffed)"] = line.ActiveUnits;
            }
        }
    }
}
