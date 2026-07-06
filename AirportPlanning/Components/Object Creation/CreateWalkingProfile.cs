using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using AirportPlanning.Core.Walking;

namespace AirportPlanning.Components.Object_Creation
{
    public class CreateWalkingProfile : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CreateWalkingProfile class.
        /// </summary>
        public CreateWalkingProfile()
          : base("Walking Profile", "WP",
        "Defines the passenger walking speed profile.",
        "Airport Planning", "Object Creation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
                    pManager.AddNumberParameter(
                "Average Speed",
                "AS",
                "Average passenger walking speed (m/s).",
                GH_ParamAccess.item,
                1.35);

            pManager.AddNumberParameter(
                "Standard Deviation",
                "SD",
                "Standard deviation of the walking speed distribution.",
                GH_ParamAccess.item,
                0.0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Walking Profile",
                "WP",
                "Walking speed profile used by passenger walking simulations.",
                GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double averageSpeed = 1.35;
            double standardDeviation = 0.0;

            if (!DA.GetData(0, ref averageSpeed))
                return;

            if (!DA.GetData(1, ref standardDeviation))
                return;

            WalkingProfile profile =
                new WalkingProfile
                {
                    AverageSpeed = averageSpeed,
                    StandardDeviation = standardDeviation
                };

            DA.SetData(
                0,
                profile);
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
            get { return new Guid("6A51A709-1CE1-449B-8B66-198C4676D25E"); }
        }
    }
}