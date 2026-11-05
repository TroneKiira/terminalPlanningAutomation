using AirportPlanning.Core.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.PassengerAnalysis
{
    /// <summary>
    /// Lists the passengers each server pool served at one facility, read from
    /// the per-facility history on the passenger.
    ///
    /// This is the third piece beside the facility component's Pool Names and
    /// Physical/Active per Pool outputs: which pool, how many units, and who.
    /// </summary>
    public class PoolPassengersComponent : GH_Component
    {
        public PoolPassengersComponent()
          : base("Pool Passengers", "POOLP",
            "Passenger indices per server pool at one facility, aligned with that facility's Pool Names.",
            "Airport Planning", "Analysis")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "Passenger population carrying per-facility history.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Facility Type", "F",
                "0=CheckIn, 1=BagDrop, 2=Security, 3=Emigration, 4=Verification, " +
                "5=Departure Lounge, 6=Boarding, 7=Terminal Entry, 8=Immigration, " +
                "9=Transfer Desk, 10=Baggage Recheck.",
                GH_ParamAccess.item,
                0);

            pManager.AddBooleanParameter(
                "Skip Warm-Up", "SKIP",
                "When true, warm-up passengers are excluded, matching the pool metrics.",
                GH_ParamAccess.item,
                true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Pool Names", "PN",
                "Pool names in order of first appearance in the wired population.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Passenger Count", "N",
                "Passengers served per pool.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Passenger Indices", "PIDX",
                "Indices into the wired Passengers list, one branch per pool in Pool Names order.",
                GH_ParamAccess.tree);        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> passengers = new List<Passenger>();
            if (!DA.GetDataList(0, passengers)) return;

            int facilityIndex = 0;
            DA.GetData(1, ref facilityIndex);
            bool skipWarmUp = true;
            DA.GetData(2, ref skipWarmUp);

            FacilityType facility = (FacilityType)facilityIndex;

            // Keep each passenger's position in the wired list while filtering,
            // so the indices stay usable downstream.
            var served = passengers
                .Select((passenger, index) => new { passenger, index })
                .Where(x => x.passenger != null &&
                            x.passenger.History != null &&
                            x.passenger.History.ContainsKey(facility))
                .Where(x => !skipWarmUp || !x.passenger.IsWarmUp)
                .Select(x => new
                {
                    x.index,
                    PoolName = x.passenger.History[facility].PoolName ?? string.Empty
                })
                .ToList();

            List<string> names = new List<string>();
            List<int> counts = new List<int>();
            GH_Structure<GH_Integer> indices = new GH_Structure<GH_Integer>();

            foreach (var group in served
                .GroupBy(x => string.IsNullOrWhiteSpace(x.PoolName) ? "(no pool)" : x.PoolName)
                .OrderBy(g => g.Min(x => x.index)))
            {
                names.Add(group.Key);
                counts.Add(group.Count());

                GH_Path path = new GH_Path(names.Count - 1);

                foreach (var item in group)
                    indices.Append(new GH_Integer(item.index), path);
            }

            DA.SetDataList(0, names);
            DA.SetDataList(1, counts);
            DA.SetDataTree(2, indices);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("C7A1E4B2-9F36-4D58-8A21-5E0B3C6D7F94"); }
        }
    }
}
