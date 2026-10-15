using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Components.PassengerAnalysis
{
    /// <summary>
    /// Sorts a passenger population chronologically by arrival time. Useful
    /// after merging filtered flows (filtering preserves order; a Grasshopper
    /// Merge concatenates, so the merged list is grouped rather than
    /// chronological). Simulation results do not depend on list order — every
    /// facility re-sorts internally — this is for downstream inspection.
    /// </summary>
    public class SortPassengersComponent : GH_Component
    {
        public SortPassengersComponent()
          : base("Sort Passengers by Arrival", "SORT",
            "Sorts a passenger population chronologically by arrival time.",
            "Airport Planning", "Analysis")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "The passenger population to sort.",
                GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "The same passengers, ordered by arrival time.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> passengers = new List<Passenger>();

            if (!DA.GetDataList(0, passengers)) return;

            List<Passenger> sorted = passengers
                .OrderBy(p => p.ArrivalTime)
                .ToList();

            DA.SetDataList(0, sorted);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("C4D5E6F7-0819-4ABC-DEF0-123456789012"); }
        }
    }
}
