using System;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Standards;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class SecuritySizer : FacilitySizerBase
    {
        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards is SecurityStandards secStd)
            {
                int xrayCount = Context.OperationalData.ProcessorCount;

                if (secStd.IsGenderSegregated && Context.PassengerSummary != null)
                {
                    double totalPax = Context.PassengerSummary.TotalPassengers;
                    double maleRatio = totalPax > 0 ? (double)Context.PassengerSummary.MalePassengers / totalPax : 0.70;
                    double femaleRatio = totalPax > 0 ? (double)Context.PassengerSummary.FemalePassengers / totalPax : 0.30;

                    double boothsPerXray = secStd.XRayThroughput / secStd.FriskingThroughput;
                    int totalFriskingBooths = (int)Math.Ceiling(xrayCount * boothsPerXray);

                    int maleBooths = (int)Math.Round(totalFriskingBooths * maleRatio);
                    int femaleBooths = totalFriskingBooths - maleBooths;

                    double xRayArea = xrayCount * (secStd.Pitch * (secStd.Depth + secStd.DivestRecomposeAllowance));
                    double boothArea = (maleBooths + femaleBooths) * (secStd.FriskingBoothWidth * secStd.FriskingBoothDepth);

                    procZone.Area = (xRayArea + boothArea) * 1.20;

                    Result.EquipmentCounts["Physical X-Rays"] = xrayCount;
                    Result.EquipmentCounts["Male WTMDs"] = maleBooths;
                    Result.EquipmentCounts["Female WTMDs"] = femaleBooths;

                    // 🚨 Add Active Lanes
                    Result.EquipmentCounts["Active X-Ray Lanes"] = Context.OperationalData.ActiveProcessorCount;

                    int pairedIslands = (int)Math.Ceiling(xrayCount / 2.0);
                    Result.PlanningAdvice.Add(new PlanningAdvice(
                        "Paired Island Layout",
                        $"Consider grouping the {xrayCount} X-Rays into {pairedIslands} 'Paired Islands'. " +
                        $"Each island will share an average of {Math.Round(boothsPerXray * 2, 1)} WTMD/Frisking points.",
                        "Info"));
                }
                else
                {
                    double laneArea = secStd.Pitch * (secStd.Depth + secStd.DivestRecomposeAllowance);
                    procZone.Area = xrayCount * laneArea;
                    Result.EquipmentCounts["Physical Security Lanes"] = xrayCount;
                    Result.EquipmentCounts["Active Security Lanes"] = Context.OperationalData.ActiveProcessorCount;
                }
            }
        }

        protected override FacilitySizingResult BuildResult()
        {
            var result = base.BuildResult();
            result.PreferredAxis = PreferredAxis.Major;
            return result;
        }
    }
}