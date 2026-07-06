using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Walking;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    public class WalkingComponent : GH_Component
    {
        public WalkingComponent()
          : base("Walking", "WLK", "Simulates passenger movement between airport facilities.", "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population entering the walking segment.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Distance", "D", "Walking distance between two passenger processing facilities (metres).", GH_ParamAccess.item);
            pManager.AddGenericParameter("Walking Profile", "WP", "Passenger walking speed profile.", GH_ParamAccess.item);
            pManager.AddTextParameter("From Facility", "F", "Name of the facility from which passengers are departing.", GH_ParamAccess.item);
            pManager.AddTextParameter("To Facility", "T", "Name of the facility to which passengers are travelling.", GH_ParamAccess.item);

            // Made Facility names optional to avoid annoying warnings if left blank
            pManager[3].Optional = true;
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population after completing the walking segment.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            double distance = 0.0;
            WalkingProfile walkingProfile = null;
            string fromFacility = string.Empty;
            string toFacility = string.Empty;

            if (!DA.GetDataList(0, inPassengers)) return;
            if (!DA.GetData(1, ref distance)) return;
            if (!DA.GetData(2, ref walkingProfile)) return;
            DA.GetData(3, ref fromFacility);
            DA.GetData(4, ref toFacility);

            // FIX: Deep clone so moving the Distance slider doesn't permanently ruin the upstream passenger data!
            List<Passenger> passengers = inPassengers.Select(p => p.Clone()).ToList();

            WalkingInputs input = new WalkingInputs
            {
                Passengers = passengers,
                Distance = distance,
                WalkingProfile = walkingProfile,
                FromFacility = fromFacility,
                ToFacility = toFacility
            };

            WalkingSolver solver = new WalkingSolver();
            List<Passenger> results = solver.Solve(input);

            DA.SetDataList(0, results);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("124E0287-7498-4004-83DD-18137984F662"); } }
    }
}