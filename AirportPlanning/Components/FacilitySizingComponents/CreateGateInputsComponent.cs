using AirportPlanning.Core.Gates;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    /// <summary>
    /// PHP peak-hour movements and stand occupancy, per aircraft code.
    /// </summary>
    public class CreateGateInputsComponent : GH_Component
    {
        public CreateGateInputsComponent()
          : base("Gate Inputs", "G-IN",
             "PHP peak-hour movements and stand occupancy by code. Lounge profiles are supplied separately to Gate & Stand Sizer.",
            "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Codes", "C",
                "Annex 14 code letter for each row: A, B, C, D, E or F.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Domestic ATMs", "ADM",
                "Arriving movements in the domestic peak hour, for each code.",
                GH_ParamAccess.list);
            pManager[1].Optional = true;

            pManager.AddIntegerParameter(
                "International ATMs", "AIM",
                "Arriving movements in the international peak hour, for each code.",
                GH_ParamAccess.list);
            pManager[2].Optional = true;

            pManager.AddNumberParameter(
                "Domestic Occupancy Min", "DOCC",
                "Minutes an aircraft of each code holds a stand in the domestic sector.",
                GH_ParamAccess.list);
            pManager[3].Optional = true;

            // Appended after the original PHP inputs to keep the domestic
            // occupancy wire stable in existing definitions.
            pManager.AddNumberParameter(
                "International Occupancy Min", "IOCC",
                "Minutes an aircraft of each code holds a stand in the international sector.",
                GH_ParamAccess.list);
            pManager[4].Optional = true;
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
            List<double> domesticOccupancy = new List<double>();
            List<double> internationalOccupancy = new List<double>();

            DA.GetDataList(0, codes);
            DA.GetDataList(1, domesticAtms);
            DA.GetDataList(2, internationalAtms);
            DA.GetDataList(3, domesticOccupancy);
            DA.GetDataList(4, internationalOccupancy);

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
                    DomesticOccupancyMinutes = ValueAt(domesticOccupancy, i),
                    InternationalOccupancyMinutes = ValueAt(internationalOccupancy, i)
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
