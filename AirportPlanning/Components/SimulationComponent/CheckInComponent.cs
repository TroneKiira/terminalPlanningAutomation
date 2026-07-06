using System;
using System.Collections.Generic;
using System.Linq;

using Grasshopper.Kernel;
using Rhino.Geometry;

using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Walking;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities.CheckIn;

namespace AirportPlanning.Components.SimulationComponent
{
    public class CheckInComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CheckInComponent class.
        /// </summary>
        public CheckInComponent()
          : base("Check-In", "CHK",
            "Simulates passenger processing at the check-in facility.",
            "Airport Planning", "Simulation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(
     GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers",
                "P",
                "Passenger population entering the check-in facility.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Service Profile",
                "SP",
                "Check-in service time profile.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Check-In Counters",
                "C",
                "Number of available check-in counters.",
                GH_ParamAccess.item,
                8);

            pManager.AddBooleanParameter(
                "Optimize Counters",
                "O",
                "Automatically determine the required number of counters.",
                GH_ParamAccess.item,
                false);

            pManager.AddNumberParameter(
                "Target Wait",
                "TW",
                "Target 95th percentile waiting time (minutes).",
                GH_ParamAccess.item,
                10.0);
            pManager.AddNumberParameter("Staffing Percentage", "S%", "Percentage of physical desks actively staffed.", GH_ParamAccess.item, 100.0);

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
        protected override void RegisterOutputParams(
    GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers",
                "P",
                "Processed passenger population after check-in.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Check-In Passengers",
                "CP",
                "Passengers that required check-in.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Queue Results",
                "QR",
                "Complete check-in simulation results.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Counters Used",
                "C",
                "Number of check-in counters used in the simulation.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Peak Queue Length",
                "PQ",
                "Maximum queue length observed during the simulation.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Average Wait",
                "AW",
                "Average passenger waiting time (minutes).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Maximum Wait",
                "MW",
                "Maximum passenger waiting time (minutes).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "95th Percentile Wait",
                "W95",
                "95th percentile passenger waiting time (minutes).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Counter Utilization",
                "U",
                "Utilization of each check-in counter.",
                GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(
     IGH_DataAccess DA)
        {
            List<Passenger> passengers =
                new List<Passenger>();

            ServiceProfile serviceProfile =
                null;

            int counters = 0;
            bool optimize = false;
            double targetWait = 0.0;
            int seed = 1;
            double staffingPercentage = 0;

            if (!DA.GetDataList(0, passengers))
                return;

            if (!DA.GetData(1, ref serviceProfile))
                return;

            if (!DA.GetData(2, ref counters))
                return;

            if (!DA.GetData(3, ref optimize))
                return;

            if (!DA.GetData(4, ref targetWait))

            if (!DA.GetData(5, ref staffingPercentage))
                    return;
          
            if (!DA.GetData(6, ref seed))
                return;

            

            CheckInInputs input =
                new CheckInInputs
                {
                    Passengers = passengers,
                    ServiceProfile = serviceProfile,
                    NumberOfResources = counters,
                    OptimizeResources = optimize,
                    TargetMaximumWait = targetWait,
                    Seed = seed,
                    StaffingPercentage = staffingPercentage
                };

            CheckInSolver solver =
                new CheckInSolver();

            QueueResults results =
                solver.Solve(input);

            DA.SetDataList(
                0,
                results.Passengers);

            DA.SetDataList(
                1,
                results.ProcessedPassengers);

            DA.SetData(
                2,
                results);

            DA.SetData(
                3,
                results.Resources.Count);

            DA.SetData(
                4,
                results.PeakQueueLength);

            DA.SetData(
                5,
                results.AverageWait);

            DA.SetData(
                6,
                results.MaxWait);

            DA.SetData(
                7,
                results.Wait95);

            DA.SetDataList(
                8,
                results.CounterUtilization);
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
            get { return new Guid("8427233C-E67D-4A10-A647-BA8D420BA2F9"); }
        }
    }
}