using AirportPlanning.Core.Gates;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    /// <summary>
    /// Creates the single per-code passenger/lounge profile input shared by
    /// PHP and schedule-based gate stand demand.
    /// </summary>
    public class CreateGateLoungeProfilesComponent : GH_Component
    {
        public CreateGateLoungeProfilesComponent()
            : base("Gate Lounge Profiles", "G-LPROF",
                "One table of passenger and lounge assumptions per aircraft code. Connect the profiles to Gate & Stand Sizer.",
                "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Profile Table", "TABLE",
                "Comma-separated table: first row has Code A..F columns; rows define Average seats, Load factor (%), Seated (%), Standing (%), Next-seat allowance (%), Open-lounge reduction (%), Boarding podiums and In boarding queue (%).",
                GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Lounge Profiles", "PROF",
                "One passenger/lounge profile per aircraft code.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Warnings", "WARN",
                "Profile table validation messages.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> lines = new List<string>();
            DA.GetDataList(0, lines);

            List<GateLoungeProfile> profiles = GateLoungeProfileTableParser.Parse(lines)
                .Values
                .OrderBy(profile => profile.Code, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<string> warnings = new List<string>();
            if (profiles.Count == 0)
            {
                warnings.Add("No per-code lounge profiles were parsed. Gate stands can still be sized, but lounge areas will be zero until profiles are supplied.");
            }

            DA.SetDataList(0, profiles);
            DA.SetDataList(1, warnings);

            foreach (string warning in warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("A3270C96-9AF8-48B1-A591-1CEEB1675AF4"); }
        }
    }
}
