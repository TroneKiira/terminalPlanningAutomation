using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using AirportPlanning.Facilities.CheckIn;

namespace AirportPlanning.Components.Object_Creation
{
    public class CreateArrivalProfile : GH_Component
    {
        /// <summary>
        /// Default peak, in minutes before departure. Chosen to sit comfortably
        /// inside a typical 120-180 minute simulation horizon.
        /// </summary>
        public const double DefaultPeakMinutes = 60.0;

        /// <summary>
        /// Default kernel width. Deliberately wide: a narrow sigma concentrates
        /// the whole design-hour count into only a few minutes, which produces a
        /// very large expected arrival count per minute.
        /// </summary>
        public const double DefaultSigmaMinutes = 30.0;

        /// <summary>
        /// Initializes a new instance of the ArrivalProfile class.
        /// </summary>
        public CreateArrivalProfile()
          : base("Arrival Profile", "AP",
        "Defines the passenger arrival distribution before departure.",
        "Airport Planning", "Object Creation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter(
               "Peak",
               "P",
               "Peak arrival time before departure where passenger arrivals are most concentrated (minutes before departure).",
               GH_ParamAccess.item,
               DefaultPeakMinutes);

            pManager.AddNumberParameter(
                "Sigma",
                "S",
                "Standard deviation controlling the spread of passenger arrivals (minutes). Wider values spread the same demand over more minutes.",
                GH_ParamAccess.item,
                DefaultSigmaMinutes);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
            "Arrival Profile",
            "AP",
            "Passenger arrival distribution profile.",
            GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double peak = DefaultPeakMinutes;
            double sigma = DefaultSigmaMinutes;

            DA.GetData(0, ref peak);
            DA.GetData(1, ref sigma);

            if (sigma <= 0.0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Sigma must be greater than 0; using the default {DefaultSigmaMinutes:0.#} minutes.");
                sigma = DefaultSigmaMinutes;
            }

            if (peak <= 0.0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Peak must be greater than 0; using the default {DefaultPeakMinutes:0.#} minutes.");
                peak = DefaultPeakMinutes;
            }

            ArrivalProfile profile =
                new ArrivalProfile
                {
                    Peak = peak,
                    Sigma = sigma
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
            get { return new Guid("9DCEB8B4-B02F-4B64-A7B8-4230725C74BE"); }
        }
    }
}