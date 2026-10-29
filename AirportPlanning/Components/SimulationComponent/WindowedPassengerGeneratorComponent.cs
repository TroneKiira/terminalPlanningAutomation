using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Walking;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    public class WindowedPassengerGeneratorComponent : GH_Component
    {
        public WindowedPassengerGeneratorComponent()
          : base("Windowed Passenger Generator", "WPG",
            "Generates passengers from a multi-hour demand profile. Demand Basis selects legacy terminal-arrival demand or flight-hour demand by sampled STD/STA; warm-up applies to the legacy basis only.",
            "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Domestic Departure", "DD",
                "Passenger group for domestic departure passengers.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Domestic Arrival", "DA",
                "Optional passenger group for domestic arrival passengers. Leave unwired to model departures only.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "International Departure", "ID",
                "Passenger group for international departure passengers.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "International Arrival", "IA",
                "Optional passenger group for international arrival passengers. Leave unwired to model departures only.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Walking Profile", "WP",
                "Walking speed profile for passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Random Seed", "S",
                "Random number seed used to ensure repeatable generation.",
                GH_ParamAccess.item, 1);

            pManager.AddNumberParameter(
                "Schedule Variability", "SV",
                "Random variability applied to each wave instant (minutes, +/-).",
                GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter(
                "Walk Dispersion", "WD",
                "Time over which each wave disperses after alighting (minutes, +/-).",
                GH_ParamAccess.item, 0.0);

            pManager.AddGenericParameter(
                "Show-Up Profile", "SUP",
                "Optional show-up profile for the whole run (Leisure, Business, Mix, or a custom W15/W30/W60 fit). " +
                "Shapes departures across each profile's STD-relative advance interval. Leave unwired to use the profile-specific curve or the Mix fallback.",
                GH_ParamAccess.item);
            pManager[8].Optional = true;

            pManager.AddGenericParameter(
                "Channel Routing", "CHR",
                "Optional channel routing map. When wired it assigns a channel to each passenger at generation; otherwise the built-in routing (Counter / BagDrop / Web) is used.",
                GH_ParamAccess.item);
            pManager[9].Optional = true;

            pManager.AddGenericParameter(
                "Check-In Windows", "CIW",
                "Optional domestic/international check-in lead times. Used by flight-hour demand, which derives each passenger's check-in opening and last-admission time from the sampled STD. Leave unwired for the sector defaults (Domestic -120/-60, International -180/-60).",
                GH_ParamAccess.item);
            pManager[10].Optional = true;

            pManager.AddNumberParameter(
                "Arrival Delay Min", "ADm",
                "Minimum minutes from STA to the first arrival process for flight-hour arrival passengers.",
                GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter(
                "Arrival Delay Max", "ADM",
                "Maximum minutes from STA to the first arrival process for flight-hour arrival passengers.",
                GH_ParamAccess.item, 30.0);

            // DA (index 1) and IA (index 3) are optional: departures-only runs
            // wire just DD and ID.
            pManager[1].Optional = true;
            pManager[3].Optional = true;

            pManager.AddGenericParameter(
                "Transfer Demand Profile", "TDP",
                "Optional transfer assumptions from Transfer Demand Profile.",
                GH_ParamAccess.item);
            pManager[13].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "Generated passenger population ordered by arrival time (tagged with demand step and warm-up flag).",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Passenger Generation Result", "PGR",
                "Complete passenger generation results.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Arrival Simulations", "AS",
                "Arrival simulations per passenger type and demand step, each carrying its per-minute histogram, step offset and design-hour / warm-up flags. Feed an Arrival Chart.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Transfer Demand", "TRD",
                "Requested and effective transfer totals by market and scenario.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            PassengerGroup domesticDeparture = null;
            PassengerGroup domesticArrival = null;
            PassengerGroup internationalDeparture = null;
            PassengerGroup internationalArrival = null;
            WalkingProfile walkingProfile = null;
            int seed = 1;
            double variability = 0.0;
            double dispersion = 0.0;
            ShowUpProfile showUpProfile = null;
            IChannelAssigner channelAssigner = null;
            CheckInWindowRules checkInWindows = null;
            double arrivalDelayMin = 0.0;
            double arrivalDelayMax = 30.0;
            TransferDemandProfile transferDemand = null;

            if (!DA.GetData(0, ref domesticDeparture)) return;
            DA.GetData(1, ref domesticArrival);
            if (!DA.GetData(2, ref internationalDeparture)) return;
            DA.GetData(3, ref internationalArrival);
            if (!DA.GetData(4, ref walkingProfile)) return;
            if (!DA.GetData(5, ref seed)) return;
            if (!DA.GetData(6, ref variability)) return;
            if (!DA.GetData(7, ref dispersion)) return;
            DA.GetData(8, ref showUpProfile);
            DA.GetData(9, ref channelAssigner);
            DA.GetData(10, ref checkInWindows);
            if (!DA.GetData(11, ref arrivalDelayMin)) return;
            if (!DA.GetData(12, ref arrivalDelayMax)) return;
            DA.GetData(13, ref transferDemand);

            if (domesticDeparture == null || internationalDeparture == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Domestic Departure and International Departure groups are required. " +
                    "Domestic Arrival and International Arrival are optional.");
                return;
            }

            List<PassengerGroup> groups = new List<PassengerGroup>
            {
                domesticDeparture,
                domesticArrival,
                internationalDeparture,
                internationalArrival
            }.Where(g => g != null).ToList();

            List<string> errors = new List<string>();

            foreach (PassengerGroup group in groups)
            {
                errors.AddRange(group.GetValidationErrors());

                if (group.DHP != null)
                    errors.AddRange(group.DHP.GetValidationErrors());
            }

            errors.AddRange(ValidateSharedWindow(groups));

            WaveSettings waves = new WaveSettings
            {
                ScheduleVariabilityMinutes = variability,
                WalkDispersionMinutes = dispersion,
                Coverage = WaveCoverage.Off
            };

            errors.AddRange(waves.GetValidationErrors());

            if (showUpProfile != null)
            {
                foreach (string error in showUpProfile.GetValidationErrors())
                    errors.Add($"{showUpProfile.Name}: {error}");
            }

            CheckInWindowRules windows = checkInWindows ?? CheckInWindowRules.CreateDefault();

            errors.AddRange(windows.GetValidationErrors());

            ArrivalProcessDelay arrivalDelay = new ArrivalProcessDelay(
                arrivalDelayMin, arrivalDelayMax);

            errors.AddRange(arrivalDelay.GetValidationErrors());

            foreach (string error in errors)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);

            if (errors.Count > 0)
                return;

            MultiIntervalPassengerGenerator generator = new MultiIntervalPassengerGenerator();

            PassengerGenerationResult result = generator.Generate(
                groups,
                walkingProfile,
                waves,
                seed,
                showUpProfile,
                channelAssigner,
                message => AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, message),
                windows,
                arrivalDelay,
                transferDemand);

            DA.SetDataList(0, result.Passengers);
            DA.SetData(1, result);
            DA.SetDataList(2, result.ArrivalSimulations);
            DA.SetData(3, result.TransferDemand);
        }

        /// <summary>
        /// Every supplied group must share one analysis window: same demand
        /// basis, step count, design-hour index and warm-up timing. Demand
        /// levels and demand-factor curves may differ per type.
        /// </summary>
        private static List<string> ValidateSharedWindow(List<PassengerGroup> groups)
        {
            List<string> errors = new List<string>();

            List<PassengerGroup> supplied = groups == null
                ? new List<PassengerGroup>()
                : groups.Where(g => g != null && g.DHP != null).ToList();

            if (supplied.Count < 2)
                return errors;

            DHP reference = supplied[0].DHP;

            for (int i = 1; i < supplied.Count; i++)
            {
                DHP candidate = supplied[i].DHP;
                string label = candidate.Type.ToString();

                if (candidate.DemandBasis != reference.DemandBasis)
                    errors.Add(
                        $"Passenger group '{label}' uses demand basis " +
                        $"{candidate.DemandBasis} but '{reference.Type}' uses " +
                        $"{reference.DemandBasis}. All supplied groups must share " +
                        $"the same demand basis.");

                if (candidate.StepCount != reference.StepCount)
                    errors.Add(
                        $"Passenger group '{label}' has {candidate.StepCount} analysis steps " +
                        $"but '{reference.Type}' has {reference.StepCount}. All supplied groups " +
                        $"must share the same analysis window.");

                if (candidate.DesignHourIndex != reference.DesignHourIndex)
                    errors.Add(
                        $"Passenger group '{label}' uses design-hour index {candidate.DesignHourIndex} " +
                        $"but '{reference.Type}' uses {reference.DesignHourIndex}. All supplied groups " +
                        $"must share the same design hour.");

                if (reference.DemandBasis == DemandTimeBasis.TerminalArrival &&
                    (candidate.WarmUpEnabled != reference.WarmUpEnabled ||
                     candidate.WarmUpMinutes != reference.WarmUpMinutes))
                    errors.Add(
                        $"Passenger group '{label}' has different warm-up timing " +
                        $"(enabled={candidate.WarmUpEnabled}, minutes={candidate.WarmUpMinutes}) " +
                        $"than '{reference.Type}' (enabled={reference.WarmUpEnabled}, " +
                        $"minutes={reference.WarmUpMinutes}). All supplied groups must share " +
                        $"the same warm-up timing.");
            }

            return errors;
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("C2D3E4F5-0617-4829-AB3C-4D5E6F708192"); }
        }
    }
}
