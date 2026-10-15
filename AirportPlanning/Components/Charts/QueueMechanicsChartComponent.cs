using AirportPlanning.Components;
using AirportPlanning.Core.Charts;
using AirportPlanning.Core.Models;
using Grasshopper.Kernel;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Drawing;

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
    /// </summary>
    public class QueueMechanicsChartComponent : GH_Component
    {
        public QueueMechanicsChartComponent()
          : base("Queue Mechanics Chart", "QMechanics",
            "Explains how the queuing model works on a real run: cumulative arrivals against departures (the gap is the queue), or queue length over time.",
            "Airport Planning", "Charts")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
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
            object rawResults = null;
            string title = "";
            bool cumulative = true;

            if (!DA.GetData(0, ref rawResults))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "No Queue Results received. Connect any facility solver's Queue Results output.");
                return;
            }

            DA.GetData(1, ref title);
            DA.GetData(2, ref cumulative);

            QueueResults results = ComponentDataHelpers.Unwrap<QueueResults>(rawResults);

            if (results == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Input is not a Queue Results object.");
                return;
            }

            if (results.CumulativeFlow == null || results.CumulativeFlow.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "These results carry no queue timeline: the facility processed no passengers.");
                return;
            }

            PlotModel model = cumulative
                ? ChartRenderer.CreateCumulativeFlowModel(
                    results, string.IsNullOrWhiteSpace(title)
                        ? "Queuing Model: Cumulative Arrivals vs Departures"
                        : title)
                : ChartRenderer.CreateQueueTimelineModel(
                    results, string.IsNullOrWhiteSpace(title)
                        ? "Queuing Model: Queue Length Over Time"
                        : title);

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

        protected override Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("C3D4E5F6-A7B8-9012-CDEF-234567890123"); }
        }
    }
}
