using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using AirportPlanning.Facilities.CheckIn;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;


namespace AirportPlanning.Components.Object_Creation
{
    public class CreatePassengerProfile : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PassengerProfile class.
        /// </summary>
        public CreatePassengerProfile()
          : base("Passenger Profile", "PP",
        "Defines the characteristics and behaviour of a passenger profile.",
        "Airport Planning", "Object Creation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Name",
                "N",
                "Descriptive name of the passenger profile.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Percentage",
                "%",
                "Percentage of passengers represented by this profile.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Passenger Type",
                "PT",
                "Passenger classification (Domestic or International).",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Journey",
                "J",
                "Passenger journey type (Origin, Transfer or Destination).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Online Check-In Probability",
                "OCI",
                "Probability that passengers complete check-in before arriving at the terminal (0-1).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Checked Bag Probability",
                "CB",
                "Probability that passengers travel with checked baggage (0-1).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Service Time Multiplier",
                "STM",
                "Deprecated: per-passenger service times are now supplied by the Service Time Map. This value is kept for file compatibility and ignored.",
                GH_ParamAccess.item,
                1.0);

            // NEW
            pManager.AddNumberParameter("Male Probability", "M%", "Probability passenger is Male (0.0 to 1.0).", GH_ParamAccess.item, 0.5);

            pManager.AddGenericParameter(
                "Arrival Profile",
                "AP",
                "Optional legacy arrival profile for this passenger type. Only the legacy Passenger Generator uses it; the Windowed Passenger Generator ignores it. Leave unwired for windowed workflows.",
                GH_ParamAccess.item);
            pManager[8].Optional = true;

            pManager.AddNumberParameter(
                "Min Advance",
                "AMIN",
                "Minimum minutes before scheduled departure that this profile arrives at the terminal (windowed generation).",
                GH_ParamAccess.item,
                60.0);

            pManager.AddNumberParameter(
                "Max Advance",
                "AMAX",
                "Maximum minutes before scheduled departure that this profile arrives at the terminal (windowed generation).",
                GH_ParamAccess.item,
                150.0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
            "Passenger Profile",
            "PP",
            "Passenger behaviour profile used during passenger generation.",
            GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = string.Empty;
            double percentage = 0.0;
            int passengerType = 0;
            double maleProb = 0.5;
            int journey = 0;
            double onlineCheckInProbability = 0.0;
            double checkedBagProbability = 0.0;
            double serviceTimeMultiplier = 1.0;
            ArrivalProfile arrivalProfile = null;
            double minAdvance = 60.0;
            double maxAdvance = 150.0;

            if (!DA.GetData(0, ref name))
                return;

            if (!DA.GetData(1, ref percentage))
                return;

            if (!DA.GetData(2, ref passengerType))
                return;

            if (!DA.GetData(3, ref journey))
                return;

            if (!DA.GetData(4, ref onlineCheckInProbability))
                return;

            if (!DA.GetData(5, ref checkedBagProbability))
                return;

            if (!DA.GetData(6, ref serviceTimeMultiplier))
                return;
            if (!DA.GetData(7, ref maleProb)) return;

            // Arrival Profile is optional legacy data: null is valid and simply
            // means the legacy generator falls back to a uniform spread, while
            // the Windowed Passenger Generator ignores it entirely.
            DA.GetData(8, ref arrivalProfile);

            DA.GetData(9, ref minAdvance);
            DA.GetData(10, ref maxAdvance);

            PassengerProfile profile =
                new PassengerProfile
                {
                    Name = name,
                    Percentage = percentage,
                    Journey = (PassengerJourney)journey,
                    OnlineCheckInProbability = onlineCheckInProbability,
                    CheckedBagProbability = checkedBagProbability,
                    ServiceTimeMultiplier = serviceTimeMultiplier,
                    MaleProbability = maleProb,
                    ArrivalProfile = arrivalProfile,
                    MinAdvanceMinutes = minAdvance,
                    MaxAdvanceMinutes = maxAdvance
                };

            DA.SetData(0, profile);
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
            get { return new Guid("A9EF19C9-2A13-48F2-9437-A704CECF15CB"); }
        }
    }
}