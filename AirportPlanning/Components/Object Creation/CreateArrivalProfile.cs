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
               GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Sigma",
                "S",
                "Standard deviation controlling the spread of passenger arrivals.",
                GH_ParamAccess.item);
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
            double peak = 0.0;
            double sigma = 0.0;

            if (!DA.GetData(0, ref peak))
                return;

            if (!DA.GetData(1, ref sigma))
                return;

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