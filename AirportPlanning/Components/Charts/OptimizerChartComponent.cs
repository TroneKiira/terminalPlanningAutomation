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
    /// Pools are labelled with their full Pool Plan identity, so two sub-pools
    /// of the same channel stay distinguishable.
    /// </summary>
    public class OptimizerChartComponent : GH_Component
    {
        public OptimizerChartComponent()
          : base("Optimizer Chart", "OptChart",
            "Plots the capacity search behind processor allocation: Wait95 against server count, with the target wait and the selected capacity marked. Labelled by full pool identity.",
            "Airport Planning", "Charts")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
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

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Chart", "C",
                "Rendered chart bitmap.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Summary", "S",
                "Per-pool search result: identity, selected capacity, Wait95, target and success.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Chart Data", "D",
                "CSV rows (pool,poolKey,capacity,wait95,target,selected) for every evaluated point.",
                GH_ParamAccess.list);
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
            DA.SetDataList(1, BuildSummary(usable));
            DA.SetDataList(2, BuildData(usable));
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
                        ? "Optimizer Search by Pool"
                        : title);
            }

            ResourceSearchOutcome only = outcomes[0];

            double target = only.Plan != null ? only.Plan.TargetWait95 : 0.0;

            return ChartRenderer.CreateOptimizerSearchModel(
                only.SearchTrace,
                target,
                string.IsNullOrWhiteSpace(title)
                    ? "Optimizer Search: Wait95 vs Server Count"
                    : title,
                only.Plan);
        }

        private static string PoolName(ResourceSearchOutcome outcome)
        {
            if (outcome?.Plan == null)
                return "Shared";

            return outcome.Plan.IsSharedPool ? "Shared" : outcome.Plan.PoolIdentity;
        }

        private static string PoolKey(ResourceSearchOutcome outcome)
        {
            return outcome?.Plan != null && !string.IsNullOrWhiteSpace(outcome.Plan.PoolKey)
                ? outcome.Plan.PoolKey
                : "";
        }

        private static List<string> BuildSummary(List<ResourceSearchOutcome> outcomes)
        {
            List<string> summary = new List<string>
            {
                $"{outcomes.Count} pool(s) searched · {outcomes.Sum(o => Math.Max(0, o.Iterations))} candidate(s) evaluated"
            };

            foreach (ResourceSearchOutcome outcome in outcomes)
            {
                string range = outcome.Plan?.Range != null
                    ? outcome.Plan.Range.Normalized().ToString()
                    : "n/a";

                string target = outcome.Plan != null && outcome.Plan.TargetWait95 > 0.0
                    ? ChartText.Number(outcome.Plan.TargetWait95)
                    : "none";

                summary.Add(
                    $"{PoolName(outcome)}: selected {outcome.OptimalPhysicalResources} physical/" +
                    $"{outcome.OptimalActiveResources} active; W95 {ChartText.Number(outcome.Wait95)}; " +
                    $"target {target}; domain {range}; iterations {outcome.Iterations}; " +
                    $"{(outcome.Success ? "target met" : "target missed")}");
            }

            return summary;
        }

        private static List<string> BuildData(List<ResourceSearchOutcome> outcomes)
        {
            List<string> rows = new List<string>
            {
                "pool,poolKey,capacity,wait95,target,selected"
            };

            foreach (ResourceSearchOutcome outcome in outcomes)
            {
                if (outcome?.SearchTrace == null)
                    continue;

                double target = outcome.Plan != null ? outcome.Plan.TargetWait95 : 0.0;

                foreach (SearchTracePoint point in outcome.SearchTrace
                    .Where(p => p != null)
                    .OrderBy(p => p.Capacity))
                {
                    rows.Add(ChartText.Row(
                        PoolName(outcome),
                        PoolKey(outcome),
                        point.Capacity.ToString(),
                        ChartText.Number(point.Wait95),
                        ChartText.Number(target),
                        point.Capacity == outcome.OptimalPhysicalResources ? "1" : "0"));
                }
            }

            return rows;
        }

        protected override Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("A1B2C3D4-E5F6-7890-ABCD-EF1234567890"); }
        }
    }
}
