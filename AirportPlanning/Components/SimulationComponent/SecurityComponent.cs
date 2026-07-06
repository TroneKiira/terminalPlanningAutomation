using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Facilities.Security;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    public class SecurityComponent : GH_Component
    {
        public SecurityComponent()
          : base("Security", "SEC", "Simulates passenger processing through security screening.", "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population entering security.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Service Profile", "SP", "Security service time profile.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Security Lanes", "L", "Number of available security lanes.", GH_ParamAccess.item, 8.0);
            pManager.AddBooleanParameter("Optimize Lanes", "O", "Automatically determine required lanes.", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Target Wait", "TW", "Target 95th percentile wait time.", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Staffing Percentage", "S%", "Percentage of physical lanes staffed.", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Random Seed", "S", "Random number seed.", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population after security.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Queue Results", "QR", "Queue simulation results.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Physical Lanes", "L", "Physical lanes required/used.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            ServiceProfile serviceProfile = null;
            double lanesDbl = 0;
            bool optimize = false;
            double targetWait = 0.0;
            double staffingPercentage = 100.0;
            double seedDbl = 1.0;

            if (!DA.GetDataList(0, inPassengers)) return;
            if (!DA.GetData(1, ref serviceProfile)) return;
            if (!DA.GetData(2, ref lanesDbl)) return;
            if (!DA.GetData(3, ref optimize)) return;
            if (!DA.GetData(4, ref targetWait)) return;
            if (!DA.GetData(5, ref staffingPercentage)) return;
            if (!DA.GetData(6, ref seedDbl)) return;

            List<Passenger> passengers = inPassengers.Select(p => p.Clone()).ToList();
            int lanes = (int)Math.Round(lanesDbl);
            int seed = (int)Math.Round(seedDbl);

            SecurityInputs input = new SecurityInputs
            {
                Passengers = passengers,
                ServiceProfile = serviceProfile,
                NumberOfResources = lanes,
                OptimizeResources = optimize,
                TargetMaximumWait = targetWait,
                Seed = seed,
                StaffingPercentage = staffingPercentage
            };

            SecuritySolver solver = new SecuritySolver();
            SecurityResults finalResult = solver.Solve(input);

            DA.SetDataList(0, finalResult.QueueResults.Passengers);
            DA.SetData(1, finalResult.QueueResults);
            DA.SetData(2, finalResult.PhysicalCounters);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("5B8FD7FC-4F44-47EB-A30E-0DD0D1425FAA"); } }
    }
}