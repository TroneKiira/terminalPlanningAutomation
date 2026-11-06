using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
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
            "Generates departure passengers from a flight schedule, using each flight's STD, " +
            "profile-specific show-up windows, check-in rules and optional entry walking. " +
            "Arrival legs are ignored; arrival demand belongs to the Deplaning Profile route.",
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
                "International Departure", "ID",
                "Passenger Group for international departure passengers. A DHP is optional.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Walking Profile", "WP",
                "Optional walking speed profile. Leave unwired for 1.35 m/s with no dispersion.",
                GH_ParamAccess.item);
            pManager[3].Optional = true;

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
            pManager[8].Optional = true;

            pManager.AddGenericParameter(
                "Fallback Show-Up Profile", "FSUP",
                "Used when a Passenger Profile has no Show-Up Profile. Leave unwired for Mix.",
                GH_ParamAccess.item);
            pManager[9].Optional = true;

            pManager.AddGenericParameter(
                "Channel Routing", "CHR",
                "Optional per-profile channel routing map.",
                GH_ParamAccess.item);
            pManager[10].Optional = true;

            pManager.AddIntegerParameter(
                "Design Hour", "DH",
                "Zero-based schedule hour to tag as the design step. -1 selects the busiest generated hour.",
                GH_ParamAccess.item, -1);

            pManager.AddGenericParameter(
                "Transfer Demand Profile", "TDP",
                "Optional transfer assumptions. Design Day applies via-route eligibility, connection-time rules and schedule caps.",
                GH_ParamAccess.item);
            pManager[12].Optional = true;
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
                "ID Demand", "IDD",
                "International Departure peak-hour demand from the schedule.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "DHD (Combined)", "DHD",
                "Design-hour demand: combined departure peak-hour demand " +
                "in the same rolling window.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "DD Peak Time", "DDT",
                "Minute of day for the Domestic Departure peak.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "ID Peak Time", "IDT",
                "Minute of day for the International Departure peak.",
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

            pManager.AddGenericParameter(
                "Transfer Demand", "TRD",
                "Requested and schedule-feasible transfer totals by scenario.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FlightScheduleResult schedule = null;
            PassengerGroup domesticDeparture = null;
            PassengerGroup internationalDeparture = null;
            WalkingProfile walkingProfile = null;
            int seed = 1;
            int arrivalBasisValue = 0;
            bool applyEntryWalk = false;
            double entryWalkDistance = 0.0;
            CheckInWindowRules checkInWindows = null;
            ShowUpProfile fallbackShowUp = null;
            IChannelAssigner channelAssigner = null;
            int designHour = -1;
            TransferDemandProfile transferDemand = null;

            if (!DA.GetData(0, ref schedule) || schedule == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "No Flight Schedule received. Connect Flight Schedule Import's FS output.");
                return;
            }

            DA.GetData(1, ref domesticDeparture);
            DA.GetData(2, ref internationalDeparture);

            DA.GetData(3, ref walkingProfile);

            if (walkingProfile == null)
            {
                walkingProfile = new WalkingProfile
                {
                    AverageSpeed = 1.35,
                    StandardDeviation = 0.0
                };
            }

            DA.GetData(4, ref seed);
            DA.GetData(5, ref arrivalBasisValue);
            DA.GetData(6, ref applyEntryWalk);
            DA.GetData(7, ref entryWalkDistance);
            DA.GetData(8, ref checkInWindows);
            DA.GetData(9, ref fallbackShowUp);
            DA.GetData(10, ref channelAssigner);
            DA.GetData(11, ref designHour);
            DA.GetData(12, ref transferDemand);

            CheckGroupPort(domesticDeparture, DHPType.DomesticDeparture, "Domestic Departure");
            CheckGroupPort(internationalDeparture, DHPType.InternationalDeparture, "International Departure");

            List<PassengerGroup> groups = new List<PassengerGroup>
            {
                domesticDeparture,
                internationalDeparture
            }
            .Where(g => g != null)
            .ToList();

            if (groups.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "At least one departure Passenger Group is required. " +
                    "This generator does not accept arrival groups.");
                return;
            }

            foreach (PassengerGroup group in groups)
            {
                if (group.DHP == null && !group.Type.HasValue)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        "Every Passenger Group needs either a DHP or an explicit Type so " +
                        "its domestic/international departure type is known.");
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
                    fallbackShowUp,
                    channelAssigner,
                    designHour,
                    null,
                    transferDemand);
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
            if (result.TransferDemand != null)
            {
                summary.Add(
                    $"Transfer basis: all-day schedule departures " +
                    $"(Domestic {result.TransferDemand.DomesticDepartureTotal}, " +
                    $"International {result.TransferDemand.InternationalDepartureTotal})");
                summary.Add($"Requested Domestic outbound transfers: {result.TransferDemand.RequestedDomesticTransferPassengers}");
                summary.Add($"Requested International outbound transfers: {result.TransferDemand.RequestedInternationalTransferPassengers}");
                summary.Add($"Effective Domestic outbound transfers: {result.TransferDemand.EffectiveDomesticTransferPassengers}");
                summary.Add($"Effective International outbound transfers: {result.TransferDemand.EffectiveInternationalTransferPassengers}");
                summary.Add(
                    $"Tagged outbound transfers: {result.TransferDemand.TaggedTransferPassengers} " +
                    $"(schedule-effective {result.TransferDemand.EffectiveTransferPassengers})");
                summary.Add(
                    $"Transfer departure peak (60 min): Domestic " +
                    $"{result.TransferDemand.DomesticDepartureTransferPeak} at " +
                    $"{SchedulePassengerGenerationResult.FormatMinute(result.TransferDemand.DomesticDepartureTransferPeakAt)}, " +
                    $"International {result.TransferDemand.InternationalDepartureTransferPeak} at " +
                    $"{SchedulePassengerGenerationResult.FormatMinute(result.TransferDemand.InternationalDepartureTransferPeakAt)}");
                summary.Add(
                    $"Transfer arrival peak (60 min): Domestic " +
                    $"{result.TransferDemand.DomesticArrivalTransferPeak} at " +
                    $"{SchedulePassengerGenerationResult.FormatMinute(result.TransferDemand.DomesticArrivalTransferPeakAt)}, " +
                    $"International {result.TransferDemand.InternationalArrivalTransferPeak} at " +
                    $"{SchedulePassengerGenerationResult.FormatMinute(result.TransferDemand.InternationalArrivalTransferPeakAt)}");

                foreach (TransferScenario scenario in Enum.GetValues(typeof(TransferScenario)))
                {
                    TransferScenarioDemand flow = result.TransferDemand.ByScenario[scenario];
                    summary.Add(
                        $"{scenario.ToFlowLabel()}: {flow.EffectivePassengers} / " +
                        $"{flow.RequestedPassengers} requested; {flow.TaggedPassengers} tagged");
                }

                foreach (string usage in result.TransferDemand.ServiceUsage.CreateSummaryLines())
                    summary.Add("Daily " + usage);

                foreach (string usage in result.TransferDemand.PeakServiceUsage.CreateSummaryLines())
                    summary.Add("Peak " + usage);
            }

            DA.SetDataList(0, result.Passengers);
            DA.SetData(1, result);
            DA.SetDataList(2, result.ArrivalSimulations);
            DA.SetData(3, (double)result.DomesticDeparturePeak);
            DA.SetData(4, (double)result.InternationalDeparturePeak);
            DA.SetData(5, (double)result.CombinedPeak);
            DA.SetData(6, (double)result.DomesticDeparturePeakAt);
            DA.SetData(7, (double)result.InternationalDeparturePeakAt);
            DA.SetData(8, (double)result.CombinedPeakAt);
            DA.SetDataList(9, summary);
            DA.SetDataList(10, result.GenerationWarnings);
            DA.SetData(11, result.TransferDemand);
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
