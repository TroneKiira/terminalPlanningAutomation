using AirportPlanning.Core.Gates;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    /// <summary>
    /// The peak-hour movements and the planeload of each aircraft type.
    ///
    /// One row per Annex 14 code, read in parallel: the movements in the
    /// domestic and international peak hours, how long the aircraft holds a
    /// stand, how many seats it has, and how its passengers divide between
    /// sitting, standing and waiting to board.
    /// </summary>
    public class CreateGateInputsComponent : GH_Component
    {
        public CreateGateInputsComponent()
          : base("Gate Inputs", "G-IN",
            "Peak-hour movements and planeload per aircraft type: code, domestic and international movements, stand occupancy, seats, and how the passengers sit, stand and queue.",
            "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Codes", "C",
                "Annex 14 code letter for each row: A, B, C, D, E, F. A code B or D aircraft is folded into the next stand up.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Domestic ATMs", "ADM",
                "Arriving movements in the domestic peak hour, for each code.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "International ATMs", "AIM",
                "Arriving movements in the international peak hour, for each code.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Occupancy Minutes", "OCC",
                "Minutes an aircraft of each code holds a stand: on-block to off-block.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Seats per Aircraft", "SE",
                "Seats on one aircraft of each code.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Seated %", "SED%",
                "Share of each aircraft's planeload seated in the lounge, 0 to 1.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Standing %", "ST%",
                "Share of each aircraft's planeload standing in the lounge, 0 to 1.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Boarding Queue %", "BQ%",
                "Share of each aircraft's planeload waiting to board, 0 to 1.",
                GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Gate Inputs", "GIN",
                "One row per aircraft type.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> codes = new List<string>();
            List<int> domesticAtms = new List<int>();
            List<int> internationalAtms = new List<int>();
            List<double> occupancy = new List<double>();
            List<int> seats = new List<int>();
            List<double> seated = new List<double>();
            List<double> standing = new List<double>();
            List<double> boardingQueue = new List<double>();

            DA.GetDataList(0, codes);
            DA.GetDataList(1, domesticAtms);
            DA.GetDataList(2, internationalAtms);
            DA.GetDataList(3, occupancy);
            DA.GetDataList(4, seats);
            DA.GetDataList(5, seated);
            DA.GetDataList(6, standing);
            DA.GetDataList(7, boardingQueue);

            List<GateCodeInput> rows = new List<GateCodeInput>();

            for (int i = 0; i < codes.Count; i++)
            {
                string code = codes[i];

                if (string.IsNullOrWhiteSpace(code))
                    continue;

                rows.Add(new GateCodeInput
                {
                    Code = code.Trim(),
                    DomesticAtms = ValueAt(domesticAtms, i),
                    InternationalAtms = ValueAt(internationalAtms, i),
                    OccupancyMinutes = ValueAt(occupancy, i),
                    Seats = ValueAt(seats, i),
                    SeatedPct = ValueAt(seated, i),
                    StandingPct = ValueAt(standing, i),
                    BoardingQueuePct = ValueAt(boardingQueue, i)
                });
            }

            if (codes.Count > 0 && rows.Count != codes.Count)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Some aircraft codes were blank and were skipped.");
            }

            DA.SetDataList(0, rows);
        }

        private static int ValueAt(List<int> values, int index)
        {
            if (values == null || index < 0 || index >= values.Count)
                return 0;

            return Math.Max(0, values[index]);
        }

        private static double ValueAt(List<double> values, int index)
        {
            if (values == null || index < 0 || index >= values.Count)
                return 0.0;

            double value = values[index];

            return double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : Math.Max(0.0, value);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("F4B7D920-1C36-4A5E-8D77-2E9A4C6B8F13"); }
        }
    }
}
