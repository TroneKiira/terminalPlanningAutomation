using System;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Standards;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class BoardingSizer : FacilitySizerBase
    {
        private int _calculatedGates = 1;

        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards is BoardingStandards bStd && Context.PassengerSummary != null)
            {
                double peakHourlyPax = Context.OperationalData.Throughput > 0 ? Context.OperationalData.Throughput : Context.PassengerSummary.TotalPassengers;
                double turnaroundHours = bStd.TurnaroundTime / 60.0;

                // 1. ALWAYS CALCULATE THE BASELINE
                double baselinePaxPerFlight = bStd.CriticalAircraftCapacity * bStd.PassengerLoadFactor;
                double baselineFlightsPerHour = peakHourlyPax / baselinePaxPerFlight;
                int baselineGates = (int)Math.Ceiling((baselineFlightsPerHour * turnaroundHours) / bStd.GateUtilization);

                Context.OperationalData.BaselineProcessorCount = baselineGates;

                double actualPaxPerFlight;

                // 2. CHECK THE TOGGLE
                if (Context.OperationalData.IsOverridden)
                {
                    _calculatedGates = Context.OperationalData.ProcessorCount;

                    // REVERSE MATH: If we have 50 gates, how many pax per flight is that?
                    double actualFlightsPerHour = (_calculatedGates * bStd.GateUtilization) / turnaroundHours;
                    actualPaxPerFlight = peakHourlyPax / actualFlightsPerHour;

                    Result.PlanningAdvice.Add(new PlanningAdvice(
                        "Distributed Demand",
                        $"Manual override to {_calculatedGates} gates distributes the demand. The average hold room is now sized for {Math.Round(actualPaxPerFlight)} passengers instead of {Math.Round(baselinePaxPerFlight)}.",
                        "Info"));
                }
                else
                {
                    _calculatedGates = baselineGates;
                    actualPaxPerFlight = baselinePaxPerFlight;
                }

                // 3. MICRO SIZING (Using the dynamically calculated pax per flight!)
                double paxInHoldRoom = actualPaxPerFlight * bStd.GateShowUpRate;
                int seatedPax = (int)Math.Round(paxInHoldRoom * bStd.SeatingProvision);
                int standingPax = (int)Math.Round(paxInHoldRoom - seatedPax);

                double seatedArea = seatedPax * bStd.SeatedAreaPerPax;
                double standingArea = standingPax * bStd.StandingAreaPerPax;
                double podiumArea = bStd.Width * bStd.Depth;

                double areaPerGate = seatedArea + standingArea + podiumArea;

                procZone.Area = _calculatedGates * areaPerGate;

                // 4. OUTPUT RAW CAD DATA
                Result.EquipmentCounts["Physical Gates"] = _calculatedGates;
                Result.EquipmentCounts["Seats Per Gate"] = seatedPax;
                Result.EquipmentCounts["Standing Pax Per Gate"] = standingPax;
                Result.EquipmentCounts["Total Seats (Terminal)"] = _calculatedGates * seatedPax;
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
            result.PreferredAxis = PreferredAxis.Minor;
            result.ProcessorCount = _calculatedGates;
            return result;
        }
    }
}