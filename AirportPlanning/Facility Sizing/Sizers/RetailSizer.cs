using System;
using System.Linq;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Standards;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class RetailSizer : FacilitySizerBase
    {
        private double _actualDwellTime = 0;
        private int _retailZones = 1;

        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards is RetailStandards retStd && Context.PassengerSummary != null)
            {
                Context.OperationalData.BaselineProcessorCount = 1; // Baseline is always 1 master zone

                if (Context.OperationalData.IsOverridden)
                {
                    _retailZones = Math.Max(1, Context.OperationalData.ProcessorCount);
                }

                double domArea = Context.PassengerSummary.DomesticPassengers * retStd.BaseAreaPerDomesticPax;
                double intArea = Context.PassengerSummary.InternationalPassengers * retStd.BaseAreaPerInternationalPax;
                double baseTotalArea = domArea + intArea;

                _actualDwellTime = Context.OperationalData.AverageWait > 0 ? Context.OperationalData.AverageWait : retStd.TargetDwellTime;

                double dwellMultiplier = _actualDwellTime / retStd.TargetDwellTime;
                dwellMultiplier = Math.Max(0.5, Math.Min(2.0, dwellMultiplier));

                // Total area across all zones
                procZone.Area = baseTotalArea * dwellMultiplier;
            }
        }

        protected override void CalculateQueueArea()
        {
            var queueZone = GetZone(ZoneType.Queue);
            if (queueZone != null) queueZone.Area = 0.0;
        }

        protected override FacilitySizingResult BuildResult()
        {
            var result = base.BuildResult();
            result.ProcessorCount = _retailZones;

            if (Context.Standards is RetailStandards retStd)
            {
                double totalArea = GetZone(ZoneType.Processing)?.Area ?? 0;

                // Divide the area by the number of zones the user requested
                double areaPerZone = totalArea / _retailZones;

                int dutyFreeArea = (int)Math.Round(areaPerZone * (retStd.DutyFreePercentage / 100.0));
                int fnbArea = (int)Math.Round(areaPerZone * (retStd.FnBPercentage / 100.0));
                int specialtyArea = (int)Math.Round(areaPerZone * (retStd.SpecialtyPercentage / 100.0));
                int convenienceArea = (int)Math.Round(areaPerZone * (retStd.ConveniencePercentage / 100.0));

                result.EquipmentCounts["Total Retail Zones"] = _retailZones;
                result.EquipmentCounts["Duty-Free Area per Zone (sqm)"] = dutyFreeArea;
                result.EquipmentCounts["F&B Area per Zone (sqm)"] = fnbArea;
                result.EquipmentCounts["Specialty Area per Zone (sqm)"] = specialtyArea;
                result.EquipmentCounts["Convenience Area per Zone (sqm)"] = convenienceArea;
            }

            return result;
        }
    }
}