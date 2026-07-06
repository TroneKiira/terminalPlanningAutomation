using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Facilities;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    public class BagDropComponent : GH_Component
    {
        public BagDropComponent()
          : base("Bag Drop", "BD", "Simulates passenger processing at the bag drop facility.", "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population entering bag drop.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Service Profile", "SP", "Bag drop service time profile.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Bag Drop Counters", "C", "Number of available bag drop counters.", GH_ParamAccess.item, 8.0);
            pManager.AddBooleanParameter("Optimize Counters", "O", "Automatically determine required counters.", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Target Wait", "TW", "Target 95th percentile wait time.", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Staffing Percentage", "S%", "Percentage of physical desks actively staffed.", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Random Seed", "S", "Random number seed.", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population after bag drop.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Queue Results", "QR", "Queue simulation results.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Physical Counters", "C", "Physical counters required/used.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            ServiceProfile serviceProfile = null;
            double countersDbl = 0;
            bool optimize = false;
            double targetWait = 0.0;
            double staffingPercentage = 100.0;
            double seedDbl = 1.0;

            if (!DA.GetDataList(0, inPassengers)) return;
            if (!DA.GetData(1, ref serviceProfile)) return;
            if (!DA.GetData(2, ref countersDbl)) return;
            if (!DA.GetData(3, ref optimize)) return;
            if (!DA.GetData(4, ref targetWait)) return;
            if (!DA.GetData(5, ref staffingPercentage)) return;
            if (!DA.GetData(6, ref seedDbl)) return;

            // DEEP CLONE AND CASTING
            List<Passenger> passengers = inPassengers.Select(p => p.Clone()).ToList();
            int counters = (int)Math.Round(countersDbl);
            int seed = (int)Math.Round(seedDbl);

            BagDropInputs input = new BagDropInputs
            {
                Passengers = passengers,
                ServiceProfile = serviceProfile,
                NumberOfResources = counters,
                OptimizeResources = optimize,
                TargetMaximumWait = targetWait,
                Seed = seed,
                StaffingPercentage = staffingPercentage
            };

            BagDropSolver solver = new BagDropSolver();
            BagDropResults finalResult = solver.Solve(input);

            DA.SetDataList(0, finalResult.QueueResults.Passengers);
            DA.SetData(1, finalResult.QueueResults);
            DA.SetData(2, finalResult.PhysicalCounters);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("FDF300D4-0E41-4EB1-9A05-93EF9AA5BE3B"); } }
    }
}