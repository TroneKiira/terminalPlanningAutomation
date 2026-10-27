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
    /// Shows what the queuing model actually did, rather than why it chose a
    /// capacity - that is the Optimizer Chart's job.
    ///
    /// Two views of the same solved run:
    ///   Cumulative Flow - the classic airport capacity diagram, where the gap
    ///                     between cumulative arrivals and cumulative departures
    ///                     is the queue;
    ///   Queue Over Time - the queue building and draining.
    ///
    /// By default the facility total is plotted. Wiring Pool Filter switches to
    /// the independent channel/sector queues the solver recorded, so a Check-In
    /// facility with four Counter sub-pools no longer hides them behind one line.
    /// </summary>
    public class QueueMechanicsChartComponent : GH_Component
    {
        public QueueMechanicsChartComponent()
          : base("Queue Mechanics Chart", "QMechanics",
            "Explains how the queuing model works on a real run: cumulative arrivals against departures (the gap is the queue), or queue length over time. Optionally plots each dedicated pool queue.",
            "Airport Planning", "Charts")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Queue Results", "QR",
                "Queue Results from any facility solver.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Title", "T",
                "Chart title. Leave empty for the default.",
                GH_ParamAccess.item, "");
            pManager[1].Optional = true;

            pManager.AddBooleanParameter(
                "Cumulative Flow", "CF",
                "On: cumulative arrivals vs departures. Off: queue length over time.",
                GH_ParamAccess.item, true);

            pManager.AddTextParameter(
                "Pool Filter", "PF",
                "Optional. Empty plots the facility total. '*' plots every recorded pool; a channel name plots all of its sub-pools; a full identity such as 'Counter|DomesticDeparture|Economy' plots just that pool. Comma-separate several.",
                GH_ParamAccess.item, "");
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Chart", "C",
                "Rendered chart bitmap.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Summary", "S",
                "What the chart shows: view, source population, window, key metrics and pool targets.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Chart Data", "D",
                "CSV rows (pool,series,minute,value) for the values actually plotted.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object rawResults = null;
            string title = "";
            bool cumulative = true;
            string poolFilter = "";

            if (!DA.GetData(0, ref rawResults))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "No Queue Results received. Connect any facility solver's Queue Results output.");
                return;
            }

            DA.GetData(1, ref title);
            DA.GetData(2, ref cumulative);
            DA.GetData(3, ref poolFilter);

            QueueResults results = ComponentDataHelpers.Unwrap<QueueResults>(rawResults);

            if (results == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Input is not a Queue Results object.");
                return;
            }

            bool poolView = !string.IsNullOrWhiteSpace(poolFilter);

            List<QueuePoolResult> selected = ChartText.SelectPools(
                results.PoolResults, poolFilter);

            if (poolView && (results.PoolResults == null || results.PoolResults.Count == 0))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "These results carry no per-pool queue detail. Re-run the facility solver to record it, or leave Pool Filter empty for the facility total.");
                return;
            }

            if (poolView && selected.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"No pool matched '{poolFilter}'. Use '*' for every pool, a channel name such as Counter, or a full identity.");
                return;
            }

            if (poolView && !selected.Any(p => cumulative
                    ? p.CumulativeFlow != null && p.CumulativeFlow.Count > 0
                    : (p.QueueTimeline != null && p.QueueTimeline.Count > 0) ||
                      (p.CumulativeFlow != null && p.CumulativeFlow.Count > 0)))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "The selected pools recorded no events for this view, so there is nothing to plot.");
                return;
            }

            if (!poolView &&
                (results.CumulativeFlow == null || results.CumulativeFlow.Count == 0))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "These results carry no queue timeline: the facility processed no passengers.");
                return;
            }

            string defaultTitle;

            if (poolView)
            {
                defaultTitle = cumulative
                    ? "Queues by Pool: Cumulative Arrivals vs Departures"
                    : "Queues by Pool: Queue Length Over Time";
            }
            else
            {
                defaultTitle = cumulative
                    ? "Queuing Model: Cumulative Arrivals vs Departures"
                    : "Queuing Model: Queue Length Over Time";
            }

            PlotModel model;

            if (poolView)
            {
                model = ChartRenderer.CreatePoolQueueModel(
                    selected,
                    cumulative,
                    string.IsNullOrWhiteSpace(title) ? defaultTitle : title);
            }
            else
            {
                model = cumulative
                    ? ChartRenderer.CreateCumulativeFlowModel(
                        results, string.IsNullOrWhiteSpace(title) ? defaultTitle : title)
                    : ChartRenderer.CreateQueueTimelineModel(
                        results, string.IsNullOrWhiteSpace(title) ? defaultTitle : title);
            }

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

            if (poolView)
            {
                DA.SetDataList(1, BuildPoolSummary(selected, cumulative));
                DA.SetDataList(2, BuildPoolData(selected, cumulative));
            }
            else
            {
                DA.SetDataList(1, BuildFacilitySummary(results, cumulative));
                DA.SetDataList(2, BuildFacilityData(results, cumulative));
            }
        }

        private static List<string> BuildFacilitySummary(
            QueueResults results,
            bool cumulative)
        {
            int processed = results.ProcessedPassengers != null
                ? results.ProcessedPassengers.Count(p => p != null && !p.IsWarmUp)
                : 0;

            string design = results.DesignStepIndex >= 0
                ? results.DesignStepIndex.ToString()
                : "none";

            List<string> summary = new List<string>
            {
                cumulative
                    ? "View: cumulative flow (facility total)"
                    : "View: queue length over time (facility total)",
                $"Processed: {processed} passengers; warm-up excluded",
                $"Window: {ChartText.Number(results.SimulationDuration)} min; design step {design}",
                $"Wait95: {ChartText.Number(results.Wait95)} min; average {ChartText.Number(results.AverageWait)}; max {ChartText.Number(results.MaxWait)}",
                $"Peak queue: {results.PeakQueueLength} waiting at {ChartText.Number(results.PeakQueueTime)} min",
                $"Activity: {ChartText.DescribeActivity(results.ProcessedPassengers, results.QueueTimeline)}",
                $"Pools recorded: {(results.PoolResults != null ? results.PoolResults.Count : 0)}"
            };

            if (results.ProcessedPassengers != null &&
                results.ProcessedPassengers.Any(p => p != null &&
                    !p.IsWarmUp &&
                    p.ArrivalTime < 0.0))
            {
                summary.Add(
                    "Time 0 is the simulation clock origin; negative minutes precede it " +
                    "(for schedule runs, check-in on the evening before midnight).");
            }

            summary.Add(cumulative
                ? "Note: the cumulative gap counts waiting plus in service; Peak queue counts waiting only."
                : "Note: this is the waiting queue; passengers already in service are not included.");

            return summary;
        }

        private static List<string> BuildPoolSummary(
            List<QueuePoolResult> pools,
            bool cumulative)
        {
            List<string> summary = new List<string>
            {
                cumulative
                    ? "View: cumulative flow by pool"
                    : "View: queue length over time by pool",
                $"{pools.Count} independent queue(s); warm-up excluded",
                "Each pool is its own queue; values are not summed or averaged across pools."
            };

            foreach (QueuePoolResult pool in pools)
            {
                summary.Add(
                    $"{pool.Name}: {pool.PassengerCount} pax; W95 {ChartText.Number(pool.Wait95)} min; " +
                    $"target {ChartText.TargetStatus(pool)}; peak queue {pool.PeakQueueLength} at " +
                    $"{ChartText.Number(pool.PeakQueueTime)} min; staffed {pool.ActiveResources}/{pool.PhysicalResources}");

                summary.Add($"  {ChartText.DescribeActivity(pool)}");
            }

            if (pools.Any(p => !double.IsNaN(p.FirstArrivalTime) && p.FirstArrivalTime < 0.0))
            {
                summary.Add(
                    "Time 0 is the simulation clock origin; negative minutes precede it " +
                    "(for schedule runs, check-in on the evening before midnight).");
            }

            if (!cumulative)
            {
                summary.Add(
                    "A flat queue line means no passenger was waiting at that minute; " +
                    "check the activity line for the last arrival and last queue event.");
            }

            return summary;
        }

        private static List<string> BuildFacilityData(
            QueueResults results,
            bool cumulative)
        {
            List<string> rows = new List<string> { "pool,series,minute,value" };

            if (cumulative)
            {
                if (results.CumulativeFlow != null)
                {
                    foreach (QueueCumulativePoint point in results.CumulativeFlow
                        .Where(p => p != null)
                        .OrderBy(p => p.Time))
                    {
                        rows.Add(ChartText.Row(
                            "FACILITY", "arrivals",
                            ChartText.Number(point.Time),
                            point.CumulativeArrivals.ToString()));

                        rows.Add(ChartText.Row(
                            "FACILITY", "departures",
                            ChartText.Number(point.Time),
                            point.CumulativeDepartures.ToString()));
                    }
                }
            }
            else if (results.QueueTimeline != null && results.QueueTimeline.Count > 0)
            {
                foreach (QueueTimelinePoint point in results.QueueTimeline
                    .Where(p => p != null)
                    .OrderBy(p => p.Time))
                {
                    rows.Add(ChartText.Row(
                        "FACILITY", "queue",
                        ChartText.Number(point.Time),
                        point.Queue.ToString()));
                }
            }
            else if (results.CumulativeFlow != null && results.CumulativeFlow.Count > 0)
            {
                // Nobody waited: the chart plots a zero line across the arrival
                // window, so publish the same two anchors here.
                List<QueueCumulativePoint> flow = results.CumulativeFlow
                    .Where(p => p != null)
                    .OrderBy(p => p.Time)
                    .ToList();

                rows.Add(ChartText.Row(
                    "FACILITY", "queue", ChartText.Number(flow[0].Time), "0"));

                rows.Add(ChartText.Row(
                    "FACILITY", "queue",
                    ChartText.Number(flow[flow.Count - 1].Time), "0"));
            }

            return rows;
        }

        private static List<string> BuildPoolData(
            List<QueuePoolResult> pools,
            bool cumulative)
        {
            List<string> rows = new List<string> { "pool,series,minute,value" };

            foreach (QueuePoolResult pool in pools)
            {
                if (cumulative)
                {
                    if (pool.CumulativeFlow == null)
                        continue;

                    foreach (QueueCumulativePoint point in pool.CumulativeFlow
                        .Where(p => p != null)
                        .OrderBy(p => p.Time))
                    {
                        rows.Add(ChartText.Row(
                            pool.Name, "arrivals",
                            ChartText.Number(point.Time),
                            point.CumulativeArrivals.ToString()));

                        rows.Add(ChartText.Row(
                            pool.Name, "departures",
                            ChartText.Number(point.Time),
                            point.CumulativeDepartures.ToString()));
                    }
                }
                else
                {
                    if (pool.QueueTimeline != null && pool.QueueTimeline.Count > 0)
                    {
                        foreach (QueueTimelinePoint point in pool.QueueTimeline
                            .Where(p => p != null)
                            .OrderBy(p => p.Time))
                        {
                            rows.Add(ChartText.Row(
                                pool.Name, "queue",
                                ChartText.Number(point.Time),
                                point.Queue.ToString()));
                        }
                    }
                    else if (pool.CumulativeFlow != null && pool.CumulativeFlow.Count > 0)
                    {
                        // No waiting: the flat zero line spans the arrival window.
                        List<QueueCumulativePoint> flow = pool.CumulativeFlow
                            .Where(p => p != null)
                            .OrderBy(p => p.Time)
                            .ToList();

                        rows.Add(ChartText.Row(
                            pool.Name, "queue", ChartText.Number(flow[0].Time), "0"));

                        rows.Add(ChartText.Row(
                            pool.Name, "queue",
                            ChartText.Number(flow[flow.Count - 1].Time), "0"));
                    }
                }
            }

            return rows;
        }

        protected override Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("C3D4E5F6-A7B8-9012-CDEF-234567890123"); }
        }
    }
}
