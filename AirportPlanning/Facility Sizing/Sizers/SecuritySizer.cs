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
    public class SecuritySizer : FacilitySizerBase
    {
        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards is SecurityStandards secStd)
            {
                // Base X-Ray Area
                double laneArea = secStd.Pitch * (secStd.Depth + secStd.DivestRecomposeAllowance);
                double totalLaneArea = Context.OperationalData.ProcessorCount * laneArea;

                if (secStd.IsGenderSegregated && Context.PassengerSummary != null)
                {
                    // Get exact demographic ratios from the simulation
                    double totalPax = Context.PassengerSummary.TotalPassengers;
                    double maleRatio = totalPax > 0 ? Context.PassengerSummary.MalePassengers / totalPax : 0.5;
                    double femaleRatio = totalPax > 0 ? Context.PassengerSummary.FemalePassengers / totalPax : 0.5;

                    // Calculate peak hourly demand (simplified approximation from simulation throughput)
                    double peakHourlyDemand = Context.OperationalData.Throughput > 0 ? Context.OperationalData.Throughput : totalPax;

                    // Calculate total frisking booths needed
                    int totalFriskingBooths = (int)Math.Ceiling(peakHourlyDemand / secStd.FriskingThroughput);

                    // Split by exact gender ratio
                    int maleBooths = (int)Math.Ceiling(totalFriskingBooths * maleRatio);
                    int femaleBooths = (int)Math.Ceiling(totalFriskingBooths * femaleRatio);

                    double boothArea = secStd.FriskingBoothWidth * secStd.FriskingBoothDepth;
                    double totalFriskingArea = (maleBooths + femaleBooths) * boothArea;

                    procZone.Area = totalLaneArea + totalFriskingArea;

                    Result.PlanningAdvice.Add(new PlanningAdvice(
                        "CISF Equipment Breakdown",
                        $"Requires {Context.OperationalData.ProcessorCount} X-Rays, {maleBooths} Male WTMDs, and {femaleBooths} Female Enclosed Booths.",
                        "Info"));
                }
                else
                {
                    procZone.Area = totalLaneArea;
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
