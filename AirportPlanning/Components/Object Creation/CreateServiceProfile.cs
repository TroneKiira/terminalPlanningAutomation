using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using AirportPlanning.Core.Inputs;

namespace AirportPlanning.Components.Object_Creation
{
    public class CreateServiceProfile : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ServiceProfile class.
        /// </summary>
        public CreateServiceProfile()
          : base("Service Profile", "SP",
        "Defines the passenger service time distribution.",
        "Airport Planning", "Object Creation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter(
               "Mean Service Time",
               "M",
               "Average time required to process one passenger (minutes).",
               GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Standard Deviation",
                "SD",
                "Standard deviation of the passenger service time distribution.",
                GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
            "Service Profile",
            "SP",
            "Passenger service time profile used during queue simulations.",
            GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double mean = 0.0;
            double standardDeviation = 0.0;

            if (!DA.GetData(0, ref mean))
                return;

            if (!DA.GetData(1, ref standardDeviation))
                return;

            ServiceProfile profile =
                new ServiceProfile
                {
                    MeanServiceTime = mean,
                    StandardDeviation = standardDeviation
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
            get { return new Guid("9DD38D36-3A92-4BF4-AE18-32AD27BA7DC6"); }
        }
    }
}