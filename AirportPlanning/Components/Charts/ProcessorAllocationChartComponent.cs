using AirportPlanning.Components;
using AirportPlanning.Core.Charts;
using AirportPlanning.Core.Models;
using Grasshopper.Kernel;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AirportPlanning.Components.Charts
{
    /// <summary>
    /// Shows how the queue simulation allotted passengers to the staffed
    /// processors over time.
    ///
    /// Processor IDs run along the X-axis with simulation time on the Y-axis.
    /// When the solver recorded pool-level queues, each pool's own queue panel
    /// sits beside its processors, so occupancy and waiting are read together
    /// for the same dedicated channel/sector queue.
    /// </summary>
    public class ProcessorAllocationChartComponent : GH_Component
    {
        public ProcessorAllocationChartComponent()
          : base("Processor Allocation Chart", "PAlloc",
            "Plots how the simulation allotted passengers to staffed processors over time, paired with the queue for each pool.",
            "Airport Planning", "Charts")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Queue Results", "QR",
                "Queue Results from any facility solver. Processors shown are the staffed resources the simulation used.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Title", "T",
                "Chart title. Leave empty for the default.",
                GH_ParamAccess.item, "");
            pManager[1].Optional = true;

            pManager.AddBooleanParameter(
                "Show Pool Queues", "SPQ",
                "Pair each pool's processor band with that pool's own queue. Off falls back to one facility-wide queue panel.",
                GH_ParamAccess.item, true);
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Chart", "C",
                "Rendered chart bitmap.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Summary", "S",
                "Pools, processors, served passengers and queue source behind the chart.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Chart Data", "D",
                "CSV rows for every plotted service block and pool queue event, including passenger IDs.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object rawResults = null;
            string title = "";
            bool showPoolQueues = true;

            if (!DA.GetData(0, ref rawResults))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "No Queue Results received. Connect any facility solver's Queue Results output.");
                return;
            }

            DA.GetData(1, ref title);
            DA.GetData(2, ref showPoolQueues);

            QueueResults results = ComponentDataHelpers.Unwrap<QueueResults>(rawResults);

            if (results == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Input is not a Queue Results object.");
                return;
            }

            if (results.ProcessedPassengers == null || results.ProcessedPassengers.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "These results carry no processed passengers, so there is no processor allocation to plot.");
                return;
            }

            List<ProcessorTimelinePool> pools = ProcessorTimeline.Build(results);

            if (pools.All(p => p.Blocks == null || p.Blocks.Count == 0))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "No passenger has a valid service interval on a processor. Check that the facility simulated service times.");
                return;
            }

            PlotModel model = ChartRenderer.CreateProcessorAllocationModel(
                results,
                string.IsNullOrWhiteSpace(title)
                    ? "Processor Allocation: Staffed Processors Over Time"
                    : title,
                showPoolQueues);

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
            DA.SetDataList(1, BuildSummary(results, pools, showPoolQueues));
            DA.SetDataList(2, BuildData(results, pools, showPoolQueues));
        }

        private static List<string> BuildSummary(
            QueueResults results,
            List<ProcessorTimelinePool> pools,
            bool showPoolQueues)
        {
            int totalProcessors = pools.Sum(p => p.ProcessorCount);
            int blocks = pools.Sum(p => p.Blocks != null ? p.Blocks.Count : 0);
            int queuePools = results.PoolResults != null
                ? results.PoolResults.Count(p => p != null &&
                    p.QueueTimeline != null &&
                    p.QueueTimeline.Count > 0)
                : 0;

            List<string> summary = new List<string>
            {
                $"Pools: {pools.Count}; staffed processors: {totalProcessors}",
                $"Service blocks: {blocks}; warm-up passengers excluded",
                queuePools > 0
                    ? (showPoolQueues
                        ? $"Queue panels: per pool ({queuePools} with queue events)"
                        : "Queue panels: facility total (per-pool queues hidden)")
                    : "Queue panels: facility total (no per-pool detail recorded)"
            };

            foreach (ProcessorTimelinePool pool in pools)
            {
                int poolBlocks = pool.Blocks != null ? pool.Blocks.Count : 0;

                double spanStart = poolBlocks > 0 ? pool.Blocks.Min(b => b.StartTime) : 0.0;
                double spanEnd = poolBlocks > 0 ? pool.Blocks.Max(b => b.EndTime) : 0.0;
                double busy = poolBlocks > 0 ? pool.Blocks.Sum(b => b.Duration) : 0.0;
                double capacity = pool.ProcessorCount * Math.Max(0.0, spanEnd - spanStart);
                double utilization = capacity > 0.0 ? busy / capacity : 0.0;

                summary.Add(
                    $"{pool.Name}: {pool.ProcessorCount} processor(s); {poolBlocks} block(s); " +
                    $"busy {ChartText.Number(busy)} min; utilisation {utilization:P0}; " +
                    $"{ChartText.Number(spanStart)}-{ChartText.Number(spanEnd)} min");
            }

            return summary;
        }

        private static List<string> BuildData(
            QueueResults results,
            List<ProcessorTimelinePool> pools,
            bool showPoolQueues)
        {
            List<string> rows = new List<string>
            {
                "kind,pool,series,passengerId,processor,start,end,value"
            };

            foreach (ProcessorTimelinePool pool in pools)
            {
                if (pool.Blocks == null)
                    continue;

                foreach (ProcessorServiceBlock block in pool.Blocks
                    .OrderBy(b => b.StartTime)
                    .ThenBy(b => b.ProcessorId))
                {
                    rows.Add(ChartText.Row(
                        "service",
                        pool.Name,
                        block.ProfileName,
                        block.PassengerId.ToString(),
                        block.ProcessorId.ToString(),
                        ChartText.Number(block.StartTime),
                        ChartText.Number(block.EndTime),
                        ChartText.Number(block.Duration)));
                }
            }

            if (showPoolQueues && results.PoolResults != null)
            {
                foreach (QueuePoolResult pool in results.PoolResults)
                {
                    if (pool == null || pool.QueueTimeline == null)
                        continue;

                    foreach (QueueTimelinePoint point in pool.QueueTimeline
                        .Where(p => p != null)
                        .OrderBy(p => p.Time))
                    {
                        rows.Add(ChartText.Row(
                            "queue",
                            pool.Name,
                            "queue",
                            "",
                            "",
                            ChartText.Number(point.Time),
                            "",
                            point.Queue.ToString()));
                    }
                }
            }
            else if (results.QueueTimeline != null)
            {
                foreach (QueueTimelinePoint point in results.QueueTimeline
                    .Where(p => p != null)
                    .OrderBy(p => p.Time))
                {
                    rows.Add(ChartText.Row(
                        "queue",
                        "FACILITY",
                        "queue",
                        "",
                        "",
                        ChartText.Number(point.Time),
                        "",
                        point.Queue.ToString()));
                }
            }

            return rows;
        }

        protected override Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("D4E5F6A7-B8C9-4D0E-A1F2-3B4C5D6E7F80"); }
        }
    }
}
