using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Walking;
using AirportPlanning.Facilities.CheckIn;
using AirportPlanning.Facilities.Immigration;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    public class ImmigrationComponent : GH_Component
    {
        public ImmigrationComponent()
          : base("Immigration", "IMM", "Simulates passenger processing at immigration.", "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population entering immigration.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Service Profile", "SP", "Immigration service time profile.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Immigration Booths", "B", "Number of available immigration booths.", GH_ParamAccess.item, 8.0);
            pManager.AddBooleanParameter("Optimize Booths", "O", "Automatically determine required booths.", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Target Wait", "TW", "Target 95th percentile wait time.", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Staffing Percentage", "S%", "Percentage of physical booths staffed.", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Random Seed", "S", "Random number seed.", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population after immigration.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Queue Results", "QR", "Queue simulation results.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Physical Booths", "B", "Physical booths required/used.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            ServiceProfile serviceProfile = null;
            double boothsDbl = 0;
            bool optimize = false;
            double targetWait = 0.0;
            double staffingPercentage = 100.0;
            double seedDbl = 1.0;

            if (!DA.GetDataList(0, inPassengers)) return;
            if (!DA.GetData(1, ref serviceProfile)) return;
            if (!DA.GetData(2, ref boothsDbl)) return;
            if (!DA.GetData(3, ref optimize)) return;
            if (!DA.GetData(4, ref targetWait)) return;
            if (!DA.GetData(5, ref staffingPercentage)) return;
            if (!DA.GetData(6, ref seedDbl)) return;

            List<Passenger> passengers = inPassengers.Select(p => p.Clone()).ToList();
            int booths = (int)Math.Round(boothsDbl);
            int seed = (int)Math.Round(seedDbl);

            ImmigrationInputs input = new ImmigrationInputs
            {
                Passengers = passengers,
                ServiceProfile = serviceProfile,
                NumberOfResources = booths,
                OptimizeResources = optimize,
                TargetMaximumWait = targetWait,
                Seed = seed,
                StaffingPercentage = staffingPercentage
            };

            ImmigrationSolver solver = new ImmigrationSolver();
            ImmigrationResults finalResult = solver.Solve(input);

            DA.SetDataList(0, finalResult.QueueResults.Passengers);
            DA.SetData(1, finalResult.QueueResults);
            DA.SetData(2, finalResult.PhysicalCounters);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("4E63995C-ECAF-47A9-A077-A940030621B6"); } }
    }
}