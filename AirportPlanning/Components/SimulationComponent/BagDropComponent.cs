using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using AirportPlanning.Facilities;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;

namespace AirportPlanning.Components.SimulationComponent
{
    public class BagDropComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BagDropComponent class.
        /// </summary>
        public BagDropComponent()
          : base("Bag Drop", "BD",
                "Simulates passenger processing at the bag drop facility.",
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
                "Passenger population entering the bag drop facility.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Service Profile",
                "SP",
                "Bag drop service time profile.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Bag Drop Counters",
                "C",
                "Number of available bag drop counters.",
                GH_ParamAccess.item,
                8);

            pManager.AddBooleanParameter(
                "Optimize Counters",
                "O",
                "Automatically determine the required number of bag drop counters.",
                GH_ParamAccess.item,
                false);

            pManager.AddNumberParameter(
                "Target Wait",
                "TW",
                "Target 95th percentile passenger waiting time (minutes).",
                GH_ParamAccess.item,
                10.0);

            pManager.AddIntegerParameter(
                "Random Seed",
                "S",
                "Random number seed.",
                GH_ParamAccess.item,
                1);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers",
                "PAX",
                "Passenger population after bag drop processing.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Queue Results",
                "QR",
                "Queue simulation results for the bag drop facility.",
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

            ServiceProfile serviceProfile =
                null;

            int counters = 0;
            bool optimize = false;
            double targetWait = 0.0;
            int seed = 1;

            if (!DA.GetDataList(0, passengers))
                return;

            if (!DA.GetData(1, ref serviceProfile))
                return;

            if (!DA.GetData(2, ref counters))
                return;

            if (!DA.GetData(3, ref optimize))
                return;

            if (!DA.GetData(4, ref targetWait))
                return;

            if (!DA.GetData(5, ref seed))
                return;

            BagDropInputs input =
                new BagDropInputs
                {
                    Passengers = passengers,
                    ServiceProfile = serviceProfile,
                    NumberOfResources = counters,
                    OptimizeResources = optimize,
                    TargetMaximumWait = targetWait,
                    Seed = seed
                };

            BagDropSolver solver =
                new BagDropSolver();

            QueueResults results =
                solver.Solve(input);

            DA.SetDataList(
                0,
                results.Passengers);

            DA.SetData(
                1,
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
            get { return new Guid("FDF300D4-0E41-4EB1-9A05-93EF9AA5BE3B"); }
        }
    }
}