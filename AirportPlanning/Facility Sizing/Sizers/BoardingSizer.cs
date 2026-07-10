using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Standards;
using Rhino.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Rhino.UI.Controls.RenderContentMenu;

namespace AirportPlanning.Facility_Sizing.Sizers
{
    public class BoardingSizer : FacilitySizerBase
    {
        protected override void CalculateProcessingArea()
        {
            var procZone = GetZone(ZoneType.Processing);
            if (procZone != null && Context.Standards is BoardingStandards bStd && Context.PassengerSummary != null)
            {
                // --- 1. MACRO SIZING: HOW MANY GATES? ---
                double paxPerFlight = bStd.CriticalAircraftCapacity * bStd.PassengerLoadFactor;

                // Read the peak demand from the simulation
                double peakHourlyPax = Context.OperationalData.Throughput > 0
                    ? Context.OperationalData.Throughput
                    : Context.PassengerSummary.TotalPassengers;

                double flightsPerHour = peakHourlyPax / paxPerFlight;
                double turnaroundHours = bStd.TurnaroundTime / 60.0;

                // Formula: (Flights/Hr * Turnaround) / Utilization
                int totalGates = (int)Math.Ceiling((flightsPerHour * turnaroundHours) / bStd.GateUtilization);

                // --- 2. MICRO SIZING: HOLD ROOM AREA & SEATS ---
                // How many people are actually in the room?
                double paxInHoldRoom = paxPerFlight * bStd.GateShowUpRate;

                // Split into Seated vs Standing
                int seatedPax = (int)Math.Round(paxInHoldRoom * bStd.SeatingProvision);
                int standingPax = (int)Math.Round(paxInHoldRoom - seatedPax);

                // Calculate Area for ONE gate
                double seatedArea = seatedPax * bStd.SeatedAreaPerPax;
                double standingArea = standingPax * bStd.StandingAreaPerPax;
                double podiumArea = bStd.Width * bStd.Depth; // The boarding desk itself

                double areaPerGate = seatedArea + standingArea + podiumArea;

                // Total Area for ALL Gates combined
                procZone.Area = totalGates * areaPerGate;

                // --- 3. OUTPUT RAW CAD DATA TO GRASSHOPPER ---
                Result.EquipmentCounts["Total Gates"] = totalGates;
                Result.EquipmentCounts["Seats Per Gate"] = seatedPax;
                Result.EquipmentCounts["Standing Pax Per Gate"] = standingPax;
                Result.EquipmentCounts["Total Seats (Terminal)"] = totalGates * seatedPax;

                Result.PlanningAdvice.Add(new PlanningAdvice(
                    "Gate Calculation",
                    $"Demand of {peakHourlyPax} pax/hr requires {flightsPerHour:F1} flights/hr. With a {bStd.TurnaroundTime} min turnaround, {totalGates} gates are required.",
                    "Info"));
            }
        }

        protected override void CalculateQueueArea()
        {
            // Set to 0 because the queue is absorbed into the "Standing Area" calculated above.
            var queueZone = GetZone(ZoneType.Queue);
            if (queueZone != null) queueZone.Area = 0.0;
        }

        protected override FacilitySizingResult BuildResult()
        {
            var result = base.BuildResult();
            result.PreferredAxis = PreferredAxis.Minor;
            return result;
        }
    }
}
