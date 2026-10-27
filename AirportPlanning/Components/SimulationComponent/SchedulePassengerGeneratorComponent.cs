using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Schedules;
using AirportPlanning.Core.Walking;
using Grasshopper.Kernel;

namespace AirportPlanning.Components.SimulationComponent
{
    /// <summary>
    /// Schedule-driven replacement path for the Windowed Passenger Generator.
    /// It uses actual flight legs and their STD/STA, while the existing generator
    /// remains available for aggregate DHP workflows.
    /// </summary>
    public class SchedulePassengerGeneratorComponent : GH_Component
    {
        public SchedulePassengerGeneratorComponent()
          : base("Schedule Passenger Generator", "SPG",
            "Generates passengers from a flight schedule, using each flight's STD/STA, " +
            "profile-specific show-up windows, check-in rules and optional entry walking.",
            "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Flight Schedule", "FS",
                "Flight Schedule output from Flight Schedule Import.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Domestic Departure", "DD",
                "Passenger Group for domestic departure passengers. A DHP is optional; " +
                "the group's Type or input port supplies the type.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Domestic Arrival", "DA",
                "Optional Passenger Group for domestic arrival passengers.",
                GH_ParamAccess.item);
            pManager[2].Optional = true;

            pManager.AddGenericParameter(
                "International Departure", "ID",
                "Passenger Group for international departure passengers. A DHP is optional.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "International Arrival", "IA",
                "Optional Passenger Group for international arrival passengers.",
                GH_ParamAccess.item);
            pManager[4].Optional = true;

            pManager.AddGenericParameter(
                "Walking Profile", "WP",
                "Optional walking speed profile. Leave unwired for 1.35 m/s with no dispersion.",
                GH_ParamAccess.item);
            pManager[5].Optional = true;

            pManager.AddIntegerParameter(
                "Random Seed", "S",
                "Repeatable random seed.",
                GH_ParamAccess.item, 1);

            pManager.AddIntegerParameter(
                "Arrival Basis", "AB",
                "0 = the CDF time is terminal entry; 1 = it is check-in queue arrival. " +
                "The profile CDF still spans STD-MaxAdvance to STD-MinAdvance.",
                GH_ParamAccess.item, 0);

            pManager.AddBooleanParameter(
                "Apply Entry Walk", "EW",
                "Terminal Entry only: advance generated passengers from entry to check-in using " +
                "Entry Walk Distance and the walking profile. Leave false to output at terminal entry.",
                GH_ParamAccess.item, false);

            pManager.AddNumberParameter(
                "Entry Walk Distance", "EWD",
                "Walking distance from terminal entry to check-in, in metres. Used only when Apply Entry Walk is true.",
                GH_ParamAccess.item, 0.0);

            pManager.AddGenericParameter(
                "Check-In Windows", "CIW",
                "Optional Check-In Window Rules. Leave unwired for 120/60 domestic and 180/60 international defaults.",
                GH_ParamAccess.item);
            pManager[10].Optional = true;

            pManager.AddNumberParameter(
                "Arrival Delay Min", "ADM",
                "Inbound assumption: minimum minutes from STA to the first arrival process. Default 0.",
                GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter(
                "Arrival Delay Max", "ADX",
                "Inbound assumption: maximum minutes from STA to the first arrival process. Default 30.",
                GH_ParamAccess.item, 30.0);

            pManager.AddGenericParameter(
                "Fallback Show-Up Profile", "FSUP",
                "Used when a Passenger Profile has no Show-Up Profile. Leave unwired for Mix.",
                GH_ParamAccess.item);
            pManager[13].Optional = true;

            pManager.AddGenericParameter(
                "Channel Routing", "CHR",
                "Optional per-profile channel routing map.",
                GH_ParamAccess.item);
            pManager[14].Optional = true;

            pManager.AddIntegerParameter(
                "Design Hour", "DH",
                "Zero-based schedule hour to tag as the design step. -1 selects the busiest generated hour.",
                GH_ParamAccess.item, -1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "Generated passenger population ordered by facility arrival time.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Passenger Generation Result", "PGR",
                "Schedule generation result, including design hour and flight diagnostics.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Arrival Simulations", "AS",
                "Per-type, per-hour arrival simulations for the Arrival Chart.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "DD Demand", "DDD",
                "Domestic Departure peak-hour demand from the schedule.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "DA Demand", "DAD",
                "Domestic Arrival peak-hour demand from the schedule.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "ID Demand", "IDD",
                "International Departure peak-hour demand from the schedule.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "IA Demand", "IAD",
                "International Arrival peak-hour demand from the schedule.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "DHD (Combined)", "DHD",
                "Design-hour demand: combined peak-hour demand (arrivals and departures " +
                "in the same rolling window).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "DD Peak Time", "DDT",
                "Minute of day for the Domestic Departure peak.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "DA Peak Time", "DAT",
                "Minute of day for the Domestic Arrival peak.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "ID Peak Time", "IDT",
                "Minute of day for the International Departure peak.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "IA Peak Time", "IAT",
                "Minute of day for the International Arrival peak.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Combined Peak Time", "CDT",
                "Minute of day for the Combined peak.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Demand Summary", "SUM",
                "Schedule totals, design hour and demand by passenger type.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Issues", "I",
                "Skipped legs, missing profiles and timing warnings.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FlightScheduleResult schedule = null;
            PassengerGroup domesticDeparture = null;
            PassengerGroup domesticArrival = null;
            PassengerGroup internationalDeparture = null;
            PassengerGroup internationalArrival = null;
            WalkingProfile walkingProfile = null;
            int seed = 1;
            int arrivalBasisValue = 0;
            bool applyEntryWalk = false;
            double entryWalkDistance = 0.0;
            CheckInWindowRules checkInWindows = null;
            double arrivalDelayMin = 0.0;
            double arrivalDelayMax = 30.0;
            ShowUpProfile fallbackShowUp = null;
            IChannelAssigner channelAssigner = null;
            int designHour = -1;

            if (!DA.GetData(0, ref schedule) || schedule == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "No Flight Schedule received. Connect Flight Schedule Import's FS output.");
                return;
            }

            DA.GetData(1, ref domesticDeparture);
            DA.GetData(2, ref domesticArrival);
            DA.GetData(3, ref internationalDeparture);
            DA.GetData(4, ref internationalArrival);

            DA.GetData(5, ref walkingProfile);

            if (walkingProfile == null)
            {
                walkingProfile = new WalkingProfile
                {
                    AverageSpeed = 1.35,
                    StandardDeviation = 0.0
                };
            }

            DA.GetData(6, ref seed);
            DA.GetData(7, ref arrivalBasisValue);
            DA.GetData(8, ref applyEntryWalk);
            DA.GetData(9, ref entryWalkDistance);
            DA.GetData(10, ref checkInWindows);
            DA.GetData(11, ref arrivalDelayMin);
            DA.GetData(12, ref arrivalDelayMax);
            DA.GetData(13, ref fallbackShowUp);
            DA.GetData(14, ref channelAssigner);
            DA.GetData(15, ref designHour);

            CheckGroupPort(domesticDeparture, DHPType.DomesticDeparture, "Domestic Departure");
            CheckGroupPort(domesticArrival, DHPType.DomesticArrival, "Domestic Arrival");
            CheckGroupPort(internationalDeparture, DHPType.InternationalDeparture, "International Departure");
            CheckGroupPort(internationalArrival, DHPType.InternationalArrival, "International Arrival");

            List<PassengerGroup> groups = new List<PassengerGroup>
            {
                domesticDeparture,
                domesticArrival,
                internationalDeparture,
                internationalArrival
            }
            .Where(g => g != null)
            .ToList();

            if (groups.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "At least one Passenger Group is required.");
                return;
            }

            foreach (PassengerGroup group in groups)
            {
                if (group.DHP == null && !group.Type.HasValue)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        "Every Passenger Group needs either a DHP or an explicit Type so " +
                        "its domestic/international and arrival/departure type is known.");
                    return;
                }

                if (group.Profiles == null || group.Profiles.Count == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        $"{group.ResolvedType}: Passenger Group has no profiles; its flights will be skipped.");
                }
            }

            PassengerArrivalBasis arrivalBasis = arrivalBasisValue == 1
                ? PassengerArrivalBasis.CheckInQueue
                : PassengerArrivalBasis.TerminalEntry;

            ArrivalProcessDelay arrivalDelay = new ArrivalProcessDelay(
                arrivalDelayMin,
                arrivalDelayMax);

            SchedulePassengerGenerationResult result;

            try
            {
                result = new SchedulePassengerGenerator().Generate(
                    schedule,
                    groups,
                    walkingProfile,
                    seed,
                    arrivalBasis,
                    applyEntryWalk,
                    entryWalkDistance,
                    checkInWindows,
                    arrivalDelay,
                    fallbackShowUp,
                    channelAssigner,
                    designHour);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"Schedule generation failed: {ex.Message}");
                return;
            }

            foreach (string warning in result.GenerationWarnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            List<string> summary = result.CreateSummaryLines();

            summary.Add(
                $"Arrival basis: {(arrivalBasis == PassengerArrivalBasis.TerminalEntry ? "terminal entry" : "check-in queue")}");
            summary.Add(
                applyEntryWalk && arrivalBasis == PassengerArrivalBasis.TerminalEntry
                    ? $"Entry walk applied: {entryWalkDistance:0.##} m"
                    : "Entry walk: not applied");
            summary.Add($"Inbound delay assumption: STA + {arrivalDelay.MinimumMinutes:0.#}-{arrivalDelay.MaximumMinutes:0.#} min");

            DA.SetDataList(0, result.Passengers);
            DA.SetData(1, result);
            DA.SetDataList(2, result.ArrivalSimulations);
            DA.SetData(3, (double)result.DomesticDeparturePeak);
            DA.SetData(4, (double)result.DomesticArrivalPeak);
            DA.SetData(5, (double)result.InternationalDeparturePeak);
            DA.SetData(6, (double)result.InternationalArrivalPeak);
            DA.SetData(7, (double)result.CombinedPeak);
            DA.SetData(8, (double)result.DomesticDeparturePeakAt);
            DA.SetData(9, (double)result.DomesticArrivalPeakAt);
            DA.SetData(10, (double)result.InternationalDeparturePeakAt);
            DA.SetData(11, (double)result.InternationalArrivalPeakAt);
            DA.SetData(12, (double)result.CombinedPeakAt);
            DA.SetDataList(13, summary);
            DA.SetDataList(14, result.GenerationWarnings);
        }

        private void CheckGroupPort(
            PassengerGroup group,
            DHPType expected,
            string portName)
        {
            if (group == null)
                return;

            if (!group.Type.HasValue && group.DHP == null)
                return;

            if (group.ResolvedType != expected)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"{portName} received a group typed {group.ResolvedType}. " +
                    $"The group's own type is used; wire it to the matching port or change its Type.");
            }
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid =>
            new Guid("A4C8E2F1-7B39-4D6A-9E85-0C1B2D3E4F56");
    }
}
