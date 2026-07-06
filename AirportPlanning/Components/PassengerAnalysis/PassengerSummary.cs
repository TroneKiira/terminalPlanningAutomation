using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Models;
using AirportPlanning.Core.Inputs;

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
                "Number of transfer passengers.",
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
                    p => p.Journey == PassengerJourney.Transfer));

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