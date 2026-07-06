using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;


using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Walking;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities.PassengerVerification;

namespace AirportPlanning.Components.SimulationComponent
{
    public class PassengerVerificationComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PassengerVerificationComponent class.
        /// </summary>
        public PassengerVerificationComponent()
          : base("Passenger Verification", "VER",
                "Calculates required e-gates or boarding pass checks using throughput math.",
                "Airport Planning", "Analysis")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population entering verification.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Eligible Percentage", "E%", "Percentage of passengers eligible for this gate type (0.0 to 100.0).", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Throughput Per Gate", "TPG", "Theoretical maximum passengers per hour per gate.", GH_ParamAccess.item, 300.0);
            pManager.AddNumberParameter("Design Utilization", "U", "Target utilization factor (e.g. 0.85).", GH_ParamAccess.item, 0.85);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population after passing through verification.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Verification Results", "VR", "Verification calculation results.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Required Gates", "G", "Number of gates required to meet demand.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Eligible Passengers", "EP", "Number of passengers eligible for this verification type.", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> passengers = new List<Passenger>();
            double eligiblePct = 100.0;
            double throughput = 300.0;
            double utilization = 0.85;

            if (!DA.GetDataList(0, passengers)) return;
            if (!DA.GetData(1, ref eligiblePct)) return;
            if (!DA.GetData(2, ref throughput)) return;
            if (!DA.GetData(3, ref utilization)) return;

            PassengerVerificationInputs input = new PassengerVerificationInputs
            {
                Passengers = passengers,
                EligiblePercentage = eligiblePct,
                ThroughputPerGate = throughput,
                DesignUtilization = utilization
            };

            PassengerVerificationSolver solver = new PassengerVerificationSolver();
            PassengerVerificationResults results = solver.Solve(input);

            DA.SetDataList(0, results.Passengers); // Output the passengers!
            DA.SetData(1, results);
            DA.SetData(2, results.RequiredGates);
            DA.SetData(3, results.EligiblePassengers);
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
            get { return new Guid("D864F28A-3B24-487D-B259-210C5C90CE27"); }
        }
    }
}