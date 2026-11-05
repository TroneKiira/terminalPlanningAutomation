using AirportPlanning.Core.Gates;
using AirportPlanning.Core.Schedules;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    /// <summary>Builds aircraft-on-ground gate demand from a design-day schedule.</summary>
    public class CreateGateScheduleDemandComponent : GH_Component
    {
        public CreateGateScheduleDemandComponent()
            : base("Schedule Gate Demand", "G-SCHED",
                "Measures 5-minute aircraft-on-ground peaks from a flight schedule. Connect its output to Gate & Stand Sizer; it takes precedence over peak-hour ATMs.",
                "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Flight Schedule", "FS",
                "Flight Schedule output from Flight Schedule Import.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Terminal", "TERM",
                "Optional terminal to size. Leave blank to size the whole airport.",
                GH_ParamAccess.item, string.Empty);
            pManager[1].Optional = true;

            pManager.AddNumberParameter(
                "Before On-Block Min", "PRE",
                "Minutes a stand is occupied before on-block.",
                GH_ParamAccess.item, 5.0);

            pManager.AddNumberParameter(
                "After Off-Block Min", "POST",
                "Minutes a stand remains occupied after off-block.",
                GH_ParamAccess.item, 5.0);

            pManager.AddNumberParameter(
                "One-Leg Ground Min", "ONE",
                "Ground time used when the other leg of a turn is absent from the schedule.",
                GH_ParamAccess.item, 120.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Schedule Gate Demand", "SGD",
                "Schedule-derived AOG stand demand for Gate & Stand Sizer.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Code Peaks", "PEAKS",
                "Domestic, international, combined and mixed-sector AOG peaks by code.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Warnings", "WARN",
                "Excluded schedule legs and schedule-demand warnings.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            FlightScheduleResult schedule = null;
            string terminal = string.Empty;
            double before = 5.0;
            double after = 5.0;
            double oneLeg = 120.0;

            if (!DA.GetData(0, ref schedule) || schedule == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No Flight Schedule received. Connect Flight Schedule Import's FS output.");
                return;
            }

            DA.GetData(1, ref terminal);
            DA.GetData(2, ref before);
            DA.GetData(3, ref after);
            DA.GetData(4, ref oneLeg);

            GateScheduleDemand demand = GateScheduleDemandBuilder.Build(
                schedule, terminal, before, after, oneLeg);

            List<string> peaks = (demand.Codes ?? new List<GateScheduleCodeDemand>())
                .Select(c => string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: domestic {1}, international {2}, sector sum {3}, combined {4}, mixed {5}, swing {6}, dedicated {7}/{8}{9}",
                    c.Code,
                    c.DomesticPeak,
                    c.InternationalPeak,
                    c.SectorSum,
                    c.CombinedPeak,
                    c.MixedPeak,
                    c.Swing,
                    c.DedicatedDomestic,
                    c.DedicatedInternational,
                    c.MixedFloor ? " [mixed peak governs swing]" : string.Empty))
                .ToList();

            DA.SetData(0, demand);
            DA.SetDataList(1, peaks);
            DA.SetDataList(2, demand.Warnings);

            foreach (string warning in demand.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("D4E12A76-79B4-4F31-A2C9-6B8F1057C3D9"); }
        }
    }
}
