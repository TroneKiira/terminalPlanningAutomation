using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using AirportPlanning.Core.Models;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Walking;

namespace AirportPlanning.Components.SimulationComponent
{
    public class WalkingComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the WalkingComponent class.
        /// </summary>
        public WalkingComponent()
          : base("Walking", "WLK",
            "Simulates passenger movement between airport facilities.",
            "Airport Planning", "Simulation")
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
                "Passenger population entering the walking segment.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Distance",
                "D",
                "Walking distance between two passenger processing facilities (metres).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Walking Speed",
                "WS",
                "Average passenger walking speed (metres per second).",
                GH_ParamAccess.item,
                1.35);

            pManager.AddTextParameter(
                "From Facility",
                "F",
                "Name of the facility from which passengers are departing.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "To Facility",
                "T",
                "Name of the facility to which passengers are travelling.",
                GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
            "Passengers",
            "PAX",
            "Passenger population after completing the walking segment.",
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

            double distance = 0.0;
            WalkingProfile walkingProfile =
                 null;
            string fromFacility = string.Empty;
            string toFacility = string.Empty;

            if (!DA.GetDataList(0, passengers))
                return;

            if (!DA.GetData(1, ref distance))
                return;

            if (!DA.GetData(2, ref walkingProfile))
                return;

            if (!DA.GetData(3, ref fromFacility))
                return;

            if (!DA.GetData(4, ref toFacility))
                return;

            WalkingInputs input =
                new WalkingInputs
                {
                    Passengers = passengers,
                    Distance = distance,
                    WalkingProfile = walkingProfile,
                    FromFacility = fromFacility,
                    ToFacility = toFacility
                };

            WalkingSolver solver =
                new WalkingSolver();

            List<Passenger> results =
                solver.Solve(input);

            DA.SetDataList(
                0,
                results);
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
            get { return new Guid("124E0287-7498-4004-83DD-18137984F662"); }
        }
    }
}