using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using AirportPlanning.Core.Models;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Standards;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class RetailSizer : FacilitySizerBase
    {
        private double _actualDwellTime = 0;

        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards is RetailStandards retStd && Context.PassengerSummary != null)
            {
                // 1. Calculate Base Area based on Demographics
                double domArea = Context.PassengerSummary.DomesticPassengers * retStd.BaseAreaPerDomesticPax;
                double intArea = Context.PassengerSummary.InternationalPassengers * retStd.BaseAreaPerInternationalPax;
                double baseTotalArea = domArea + intArea;

                // 2. Extract Actual Dwell Time from Passenger History!
                // (We saved this in the DwellTimeComponent earlier)
                double totalDwell = 0;
                int dwellCount = 0;

                // We have to cast the passengers from the context (assuming they are passed in)
                // Since FacilitySizingContext doesn't hold the raw passenger list, we use the OperationalData.AverageWait 
                // Alternatively, if OperationalData is empty, we default to the target.
                _actualDwellTime = Context.OperationalData.AverageWait > 0 ? Context.OperationalData.AverageWait : retStd.TargetDwellTime;

                // 3. Apply Dwell Time Multiplier
                // If they dwell 90 mins instead of 60, we need 1.5x more retail space to capture the revenue!
                double dwellMultiplier = _actualDwellTime / retStd.TargetDwellTime;

                // Clamp the multiplier so it doesn't go to crazy extremes (e.g., max 2.0x, min 0.5x)
                dwellMultiplier = Math.Max(0.5, Math.Min(2.0, dwellMultiplier));

                double finalCommercialArea = baseTotalArea * dwellMultiplier;
                procZone.Area = finalCommercialArea;
            }
        }

        protected override void CalculateQueueArea()
        {
            // Retail doesn't have a centralized queue area like security.
            var queueZone = GetZone(ZoneType.Queue);
            if (queueZone != null) queueZone.Area = 0.0;
        }

        protected override FacilitySizingResult BuildResult()
        {
            var result = base.BuildResult();

            if (Context.Standards is RetailStandards retStd)
            {
                double totalArea = GetZone(ZoneType.Processing)?.Area ?? 0;

                // Calculate the exact square meters for each category
                int dutyFreeArea = (int)Math.Round(totalArea * (retStd.DutyFreePercentage / 100.0));
                int fnbArea = (int)Math.Round(totalArea * (retStd.FnBPercentage / 100.0));
                int specialtyArea = (int)Math.Round(totalArea * (retStd.SpecialtyPercentage / 100.0));
                int convenienceArea = (int)Math.Round(totalArea * (retStd.ConveniencePercentage / 100.0));

                // Output the SQUARE METERS into the Equipment Counts dictionary for Grasshopper!
                result.EquipmentCounts["Duty-Free Area (sqm)"] = dutyFreeArea;
                result.EquipmentCounts["Food & Beverage Area (sqm)"] = fnbArea;
                result.EquipmentCounts["Specialty Retail Area (sqm)"] = specialtyArea;
                result.EquipmentCounts["Convenience Area (sqm)"] = convenienceArea;

                result.PlanningAdvice.Add(new PlanningAdvice(
                    "Commercial Sizing",
                    $"Based on an average dwell time of {Math.Round(_actualDwellTime, 1)} mins, the commercial footprint was scaled by a factor of {Math.Round(_actualDwellTime / retStd.TargetDwellTime, 2)}x.",
                    "Info"));

                if (result.PreferredTopology == "Walk-Through")
                {
                    result.PlanningAdvice.Add(new PlanningAdvice(
                        "Topology Strategy",
                        "Walk-Through topology selected. Ensure 100% of passengers are routed through the Duty-Free zone immediately after clearing security.",
                        "Info"));
                }
            }

            return result;
        }
    }
}
