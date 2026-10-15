using AirportPlanning.Core.Charts;
using AirportPlanning.Core.Queueing;
using Grasshopper.Kernel;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AirportPlanning.Components.Charts
{
    /// <summary>
    /// Plots the capacity the optimizer settled on against the target wait.
    ///
    /// Takes the <c>Search Outcomes</c> output of any facility solver, so it
    /// works unchanged for check-in, bag drop, security and immigration, and
    /// draws one line per pool when the facility was solved per channel.
    /// </summary>
    public class OptimizerChartComponent : GH_Component
    {
        public OptimizerChartComponent()
          : base("Optimizer Chart", "OptChart",
            "Plots the capacity search behind processor allocation: Wait95 against server count, with the target wait and the selected capacity marked.",
            "Airport Planning", "Charts")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Search Outcomes", "SO",
                "Resource Search Outcomes from a facility solver.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Title", "T",
                "Chart title. Leave empty for the default.",
                GH_ParamAccess.item, "");
            pManager[1].Optional = true;

            pManager.AddBooleanParameter(
                "Per Channel", "PC",
                "Force one line per pool. Defaults to a single combined curve when the facility has one pool.",
                GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Chart", "C",
                "Rendered chart bitmap.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<ResourceSearchOutcome> outcomes = new List<ResourceSearchOutcome>();
            string title = "";
            bool perChannel = false;

            if (!DA.GetDataList(0, outcomes))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "No search outcomes received. Turn Optimize on for the facility and connect its Search Outcomes output.");
                return;
            }

            DA.GetData(1, ref title);
            DA.GetData(2, ref perChannel);

            List<ResourceSearchOutcome> usable = outcomes
                .Where(o => o != null && o.SearchTrace != null && o.SearchTrace.Count > 0)
                .ToList();

            if (usable.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Search outcomes carry no trace points: the facility either ran with Optimize off, or every candidate was served from cache.");
                return;
            }

            PlotModel model = BuildModel(usable, title, perChannel);

            Bitmap chart = ChartRenderer.ExportToBitmap(model);

            if (chart == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    ChartRenderer.CanRender
                        ? "Chart could not be rendered."
                        : "Chart rendering is not available on this platform: charts need the Windows build of the plugin.");
                return;
            }

            DA.SetData(0, chart);
        }

        private static PlotModel BuildModel(
            List<ResourceSearchOutcome> outcomes,
            string title,
            bool perChannel)
        {
            if (perChannel || outcomes.Count > 1)
            {
                return ChartRenderer.CreateMultiPoolOptimizerModel(
                    outcomes,
                    string.IsNullOrWhiteSpace(title)
                        ? "Optimizer Search by Channel"
                        : title);
            }

            ResourceSearchOutcome only = outcomes[0];

            double target = only.Plan != null ? only.Plan.TargetWait95 : 0.0;

            return ChartRenderer.CreateOptimizerSearchModel(
                only.SearchTrace,
                target,
                string.IsNullOrWhiteSpace(title)
                    ? "Optimizer Search: Wait95 vs Server Count"
                    : title);
        }

        protected override Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890"); }
        }
    }
}
