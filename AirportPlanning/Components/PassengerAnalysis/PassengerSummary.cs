using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Models;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Generation;

namespace AirportPlanning.Components.PassengerAnalysis
{
    public class PassengerSummary : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PassengerSummary class.
        /// </summary>
        public PassengerSummary()
          : base("Passenger Summary", "PS",
        "Summarizes the characteristics of a passenger population.",
        "Airport Planning", "Analysis")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
               "Passengers",
               "PAX",
               "Passenger population to summarize.",
               GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "Passenger Count",
                "N",
                "Total number of passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Domestic Passengers",
                "DOM",
                "Number of domestic passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "International Passengers",
                "INT",
                "Number of international passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Origin Passengers",
                "ORG",
                "Number of origin passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Transfer Passengers",
                "TRF",
                "Transfer departures, counted once by outbound market (schedule arrival segments are not double-counted).",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Destination Passengers",
                "DST",
                "Number of destination passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Online Check-In",
                "OCI",
                "Passengers who completed online check-in.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Checked Bags",
                "BAG",
                "Passengers travelling with checked baggage.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Requires Check-In",
                "CHK",
                "Passengers requiring airport check-in.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Requires Bag Drop",
                "BD",
                "Passengers requiring bag drop.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Domestic Departure",
                "DOM-D",
                "Number of domestic departure passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Domestic Arrival",
                "DOM-A",
                "Number of domestic arrival passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "International Departure",
                "INT-D",
                "Number of international departure passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "International Arrival",
                "INT-A",
                "Number of international arrival passengers.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter("Domestic→Domestic Transfer Departures", "D-D", "Domestic-outbound transfer cohort for Domestic→Domestic.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Domestic→International Transfer Departures", "D-I", "International-outbound transfer cohort for Domestic→International.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("International→Domestic Transfer Departures", "I-D", "Domestic-outbound transfer cohort for International→Domestic.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("International→International Transfer Departures", "I-I", "International-outbound transfer cohort for International→International.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Transfer Desk Passengers", "TDSK", "Transfer passengers configured to present at a transfer desk.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Separate Security Transfers", "TSECS", "Transfer passengers routed to a dedicated transfer-security pool.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Separate Immigration Transfers", "TIMMS", "Transfer passengers routed to a dedicated transfer-immigration pool.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Separate Emigration Transfers", "TEMIS", "Transfer passengers routed to a dedicated transfer-emigration pool.", GH_ParamAccess.item);
            pManager.AddIntegerParameter(
                "Transfer Bypassed Services",
                "TBYP",
                "Total services explicitly bypassed by scenario rules across tagged transfer passengers. Missing cohorts and capacity shortfalls are not counted.",
                GH_ParamAccess.item);
            pManager.AddTextParameter(
                "Transfer Service Usage",
                "TUSE",
                "Per-service required/bypassed counts for tagged transfer passengers. 'Without explicit transfer rules' is not a bypass.",
                GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> passengers =
       new List<Passenger>();

            if (!DA.GetDataList(0, passengers))
                return;

            DA.SetData(
                0,
                passengers.Count);

            DA.SetData(
                1,
                passengers.Count(
                    p => p.PassengerType == PassengerType.Domestic));

            DA.SetData(
                2,
                passengers.Count(
                    p => p.PassengerType == PassengerType.International));

            DA.SetData(
                3,
                passengers.Count(
                    p => p.Journey == PassengerJourney.Origin));

            DA.SetData(
                4,
                passengers.Count(
                    p => p.Journey == PassengerJourney.Transfer &&
                         (p.DHPType == DHPType.DomesticDeparture ||
                          p.DHPType == DHPType.InternationalDeparture)));

            DA.SetData(
                5,
                passengers.Count(
                    p => p.Journey == PassengerJourney.Destination));

            DA.SetData(
                6,
                passengers.Count(
                    p => p.UsesOnlineCheckIn));

            DA.SetData(
                7,
                passengers.Count(
                    p => p.HasCheckedBag));

            DA.SetData(
                8,
                passengers.Count(
                    p => p.RequiresCheckIn));

            DA.SetData(
                9,
                passengers.Count(
                    p => p.RequiresBagDrop));

            DA.SetData(
                10,
                passengers.Count(
                    p => p.DHPType == DHPType.DomesticDeparture));

            DA.SetData(
                11,
                passengers.Count(
                    p => p.DHPType == DHPType.DomesticArrival));

            DA.SetData(
                12,
                passengers.Count(
                    p => p.DHPType == DHPType.InternationalDeparture));

            DA.SetData(
                13,
                passengers.Count(
                    p => p.DHPType == DHPType.InternationalArrival));

            DA.SetData(14, passengers.Count(p => p.TransferScenario == TransferScenario.DomesticToDomestic &&
                (p.DHPType == DHPType.DomesticDeparture || p.DHPType == DHPType.InternationalDeparture)));
            DA.SetData(15, passengers.Count(p => p.TransferScenario == TransferScenario.DomesticToInternational &&
                (p.DHPType == DHPType.DomesticDeparture || p.DHPType == DHPType.InternationalDeparture)));
            DA.SetData(16, passengers.Count(p => p.TransferScenario == TransferScenario.InternationalToDomestic &&
                (p.DHPType == DHPType.DomesticDeparture || p.DHPType == DHPType.InternationalDeparture)));
            DA.SetData(17, passengers.Count(p => p.TransferScenario == TransferScenario.InternationalToInternational &&
                (p.DHPType == DHPType.DomesticDeparture || p.DHPType == DHPType.InternationalDeparture)));
            DA.SetData(18, passengers.Count(p => p.TransferProcessing != null && p.TransferProcessing.RequiresTransferDesk));
            DA.SetData(19, passengers.Count(p => p.TransferProcessing != null && p.TransferProcessing.SecurityRouting == Core.Inputs.TransferFacilityRouting.Separate));
            DA.SetData(20, passengers.Count(p => p.TransferProcessing != null && p.TransferProcessing.ImmigrationRouting == Core.Inputs.TransferFacilityRouting.Separate));
            DA.SetData(21, passengers.Count(p => p.TransferProcessing != null && p.TransferProcessing.EmigrationRouting == Core.Inputs.TransferFacilityRouting.Separate));

            TransferServiceUsageSummary usage = TransferServiceUsageCalculator.Calculate(passengers);
            DA.SetData(22, usage.TotalBypassed);
            DA.SetDataList(23, usage.CreateSummaryLines());
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("E8BB12AC-6743-4870-9F45-3E0B474F016E"); }
        }
    }
}
