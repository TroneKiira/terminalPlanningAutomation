using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.PassengerAnalysis
{
    public class TransferPassengerFilter : GH_Component
    {
        /// <summary>
        /// Splits a mixed passenger population into transfer and non-transfer
        /// passenger objects.
        ///
        /// The summary component reports transfer counts; this component returns
        /// the objects, so downstream queues and reporting can wire against
        /// them. Optional filters narrow the selection:
        ///
        /// - Scenario: integer 0 = Domestic→Domestic, 1 = Domestic→International,
        ///   2 = International→Domestic, 3 = International→International. -1 (unwired)
        ///   selects all transfer flows.
        /// - Service: a required transfer service ("Transfer desk", "Check-in",
        ///   "Bag drop", "Security", "Immigration", "Emigration", "Baggage
        ///   recheck"), case-insensitive. Empty = every transfer. When set, the
        ///   selection uses the shared service predicate, so an explicit
        ///   scenario-rule bypass leaves the transfer out and shows up in the
        ///   Bypassed count.
        /// - Journey leg: "Arrival" or "Departure" to restrict to the transfer
        ///   records generated for that leg. Empty = both legs.
        ///
        /// Pass-through returns everyone that was not selected, in the original
        /// population order, so rejoining selected and pass-through lists
        /// yields the population back together without duplication.
        /// </summary>
        public TransferPassengerFilter()
          : base(
                "Transfer Passenger Filter",
                "TFILTER",
                "Splits a passenger population into transfer and non-transfer objects, with " +
                "optional flow, service and journey-leg filters for queueing or reporting.",
                "Airport Planning", "Analysis")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "The mixed passenger population.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Scenario", "SCEN",
                "Optional flow filter by integer: 0 = Domestic→Domestic, 1 = Domestic→International, " +
                "2 = International→Domestic, 3 = International→International. " +
                "Leave unwired (or pass -1) to select all transfer flows.",
                GH_ParamAccess.item, -1);
            pManager[1].Optional = true;

            pManager.AddTextParameter(
                "Service", "SVC",
                "Optional required-service filter: 'Transfer desk', 'Check-in', 'Bag drop', " +
                "'Security', 'Immigration', 'Emigration' or 'Baggage recheck'. Empty selects " +
                "every transfer. An explicit scenario-rule bypass is excluded and counted as Bypassed.",
                GH_ParamAccess.item);
            pManager[2].Optional = true;

            pManager.AddTextParameter(
                "Journey Leg", "LEG",
                "Optional leg filter: 'Arrival', 'Departure' or empty for both legs.",
                GH_ParamAccess.item);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Selected PAX", "SEL", "Transfer passengers matching the filters, in population order.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Pass-through PAX", "PT", "Everyone not selected, in population order.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Selected Count", "N", "Number of selected transfer passengers.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Bypassed Count", "BYP", "Transfer passengers whose explicit scenario rules bypass the requested service (0 when no Service filter is set).", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Domestic→Domestic", "D-D", "Departure-leg transfers of the Domestic→Domestic flow in the full population.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Domestic→International", "D-I", "Departure-leg transfers of the Domestic→International flow in the full population.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("International→Domestic", "I-D", "Departure-leg transfers of the International→Domestic flow in the full population.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("International→International", "I-I", "Departure-leg transfers of the International→International flow in the full population.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            int scenarioInt = -1;
            string serviceText = string.Empty;
            string legText = string.Empty;

            if (!DA.GetDataList(0, inPassengers)) return;
            DA.GetData(1, ref scenarioInt);
            DA.GetData(2, ref serviceText);
            DA.GetData(3, ref legText);

            TransferScenario? scenario = null;
            if (scenarioInt >= 0 && scenarioInt <= 3)
            {
                scenario = (TransferScenario)scenarioInt;
            }
            else if (scenarioInt >= 0 && scenarioInt > 3)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"Scenario must be an integer from 0 to 3 (got {scenarioInt}). " +
                    "0 = Domestic→Domestic, 1 = Domestic→International, " +
                    "2 = International→Domestic, 3 = International→International.");
            }

            bool hasService = !string.IsNullOrWhiteSpace(serviceText);
            TransferService? service = ParseService(serviceText);
            if (hasService && !service.HasValue)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Service '{serviceText}' was not recognised. Use one of: Transfer desk, Check-in, " +
                    "Bag drop, Security, Immigration, Emigration, Baggage recheck.");
            }

            bool? arrivalLeg = ParseLeg(legText);
            if (!string.IsNullOrWhiteSpace(legText) && !arrivalLeg.HasValue)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Journey leg '{legText}' was not recognised. Use 'Arrival', 'Departure' or leave empty.");
            }

            List<Passenger> selected = new List<Passenger>();
            List<Passenger> passThrough = new List<Passenger>();
            int bypassed = 0;

            foreach (Passenger passenger in inPassengers.Where(p => p != null))
            {
                bool matches =
                    passenger.Journey == PassengerJourney.Transfer &&
                    (!scenario.HasValue || passenger.TransferScenario == scenario.Value) &&
                    (!arrivalLeg.HasValue || IsArrivalLeg(passenger) == arrivalLeg.Value) &&
                    (!service.HasValue ||
                     TransferServiceUsageCalculator.UsesService(passenger, service.Value));

                if (hasService && service.HasValue && !matches)
                {
                    // Report explicit scenario bypasses against the requested
                    // service so a filtered-out transfer is explainable.
                    if (passenger.Journey == PassengerJourney.Transfer &&
                        TransferServiceUsageCalculator.AppliesTo(service.Value, passenger) &&
                        TransferServiceUsageCalculator.IsBypassed(passenger, service.Value) &&
                        (!scenario.HasValue || passenger.TransferScenario == scenario.Value) &&
                        (!arrivalLeg.HasValue || IsArrivalLeg(passenger) == arrivalLeg.Value))
                    {
                        bypassed++;
                    }
                }

                if (matches)
                    selected.Add(passenger);
                else
                    passThrough.Add(passenger);
            }

            int dd = 0;
            int di = 0;
            int idd = 0;
            int ii = 0;

            // Departure-leg transfers are counted once by outbound market,
            // matching the Passenger Summary convention.
            foreach (Passenger passenger in inPassengers)
            {
                if (passenger == null ||
                    passenger.Journey != PassengerJourney.Transfer ||
                    !IsDepartureLeg(passenger) ||
                    !passenger.TransferScenario.HasValue)
                {
                    continue;
                }

                switch (passenger.TransferScenario.Value)
                {
                    case TransferScenario.DomesticToDomestic: dd++; break;
                    case TransferScenario.DomesticToInternational: di++; break;
                    case TransferScenario.InternationalToDomestic: idd++; break;
                    case TransferScenario.InternationalToInternational: ii++; break;
                }
            }

            DA.SetDataList(0, selected);
            DA.SetDataList(1, passThrough);
            DA.SetData(2, selected.Count);
            DA.SetData(3, bypassed);
            DA.SetData(4, dd);
            DA.SetData(5, di);
            DA.SetData(6, idd);
            DA.SetData(7, ii);
        }

        private static bool IsArrivalLeg(Passenger passenger)
        {
            return passenger != null &&
                   (passenger.DHPType == DHPType.DomesticArrival ||
                    passenger.DHPType == DHPType.InternationalArrival);
        }

        private static bool IsDepartureLeg(Passenger passenger)
        {
            return passenger != null &&
                   (passenger.DHPType == DHPType.DomesticDeparture ||
                    passenger.DHPType == DHPType.InternationalDeparture);
        }

        private static TransferService? ParseService(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string key = text.Trim().Replace(" ", string.Empty).ToLowerInvariant();

            switch (key)
            {
                case "transferdesk":
                case "desk":
                    return TransferService.TransferDesk;
                case "checkin":
                case "check-in":
                    return TransferService.CheckIn;
                case "bagdrop":
                    return TransferService.BagDrop;
                case "security":
                case "securityscreening":
                    return TransferService.Security;
                case "immigration":
                    return TransferService.Immigration;
                case "emigration":
                    return TransferService.Emigration;
                case "baggagerecheck":
                case "recheck":
                case "bagrecheck":
                    return TransferService.BaggageRecheck;
                default:
                    return null;
            }
        }

        private static bool? ParseLeg(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            string key = text.Trim().ToLowerInvariant();

            if (key == "arrival")
                return true;
            if (key == "departure")
                return false;

            return null;
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("9C5E3F4A-6D7F-4A1B-8C2E-3F4A5B6C7D8E"); } }
    }
}
