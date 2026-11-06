using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using AirportPlanning.Components;
using AirportPlanning.Core.Charts;
using AirportPlanning.Core.Generation;
using Grasshopper.Kernel;
using OxyPlot;

namespace AirportPlanning.Components.Charts
{
    /// <summary>
    /// Rolling 60-minute schedule demand chart, matching the TypeScript peak
    /// profile view. Two views are available: raw demand at scheduled flight
    /// times, and generated passenger arrivals after show-up/walking timing.
    /// </summary>
    public class SchedulePeakProfileChartComponent : GH_Component
    {
        public SchedulePeakProfileChartComponent()
          : base("Schedule Peak Profile", "SPP",
            "Plots rolling 60-minute departure demand from a schedule: domestic and " +
            "international departures plus the combined curve, at 5-minute steps.",
            "Airport Planning", "Charts")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Schedule Result", "SR",
                "Passenger Generation Result from the Schedule Passenger Generator.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "View", "V",
                "0 = demand at scheduled departure times (STD); " +
                "1 = generated passenger arrivals after show-up and walking.",
                GH_ParamAccess.item, 0);

            pManager.AddTextParameter(
                "Title", "T",
                "Chart title. Leave empty for the default.",
                GH_ParamAccess.item, "");
            pManager[2].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Chart", "C",
                "Rendered chart bitmap.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Peak Summary", "PS",
                "Peak demand and time for each series.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Summary", "S",
                "What the chart shows: view, sample count, time range and series peaks.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Chart Data", "D",
                "CSV rows (series,minute,value) for the values actually plotted.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object raw = null;
            int view = 0;
            string title = "";

            if (!DA.GetData(0, ref raw) || raw == null)
                return;

            DA.GetData(1, ref view);
            DA.GetData(2, ref title);

            SchedulePassengerGenerationResult result =
                ComponentDataHelpers.Unwrap<SchedulePassengerGenerationResult>(raw);

            if (result == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Input is not a Schedule Passenger Generation Result. " +
                    "Connect the Schedule Passenger Generator's PGR output.");
                return;
            }

            List<ScheduleRollingSeries> series = view == 1
                ? result.PassengerRolling
                : result.ScheduleRolling;

            if (series == null || series.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    view == 1
                        ? "No generated-passenger rolling series is available."
                        : "No schedule rolling series is available. Check the flight schedule import.");
                return;
            }

            string defaultTitle = view == 1
                ? "Passenger Arrivals: Rolling 60-min Demand"
                : "Schedule Demand: Rolling 60-min at STD";

            PlotModel model = ChartRenderer.CreateSchedulePeakProfileModel(
                series,
                string.IsNullOrWhiteSpace(title) ? defaultTitle : title);

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

            var summary = new List<string>
            {
                $"DD peak {Peak(series, s => s.DomesticDeparture)} at {PeakTime(series, s => s.DomesticDeparture)}",
                $"ID peak {Peak(series, s => s.InternationalDeparture)} at {PeakTime(series, s => s.InternationalDeparture)}",
                $"Combined peak {Peak(series, s => s.Combined)} at {PeakTime(series, s => s.Combined)}"
            };

            DA.SetDataList(1, summary);

            DA.SetDataList(2, BuildSummary(series, view, summary));
            DA.SetDataList(3, BuildData(series));
        }

        private static List<string> BuildSummary(
            List<ScheduleRollingSeries> series,
            int view,
            List<string> peaks)
        {
            List<ScheduleRollingSeries> points = series
                .Where(s => s != null)
                .OrderBy(s => s.Minute)
                .ToList();

            List<string> summary = new List<string>
            {
                view == 1
                    ? "View: generated passenger arrivals (rolling 60 min)"
                    : "View: scheduled departure demand at STD (rolling 60 min)",
                $"Samples: {points.Count}; step {Step(points)} min"
            };

            if (points.Count > 0)
            {
                summary.Add(
                    $"Window: {SchedulePassengerGenerationResult.FormatMinute(points[0].Minute)}" +
                    $" to {SchedulePassengerGenerationResult.FormatMinute(points[points.Count - 1].Minute)}");
            }

            summary.AddRange(peaks);

            return summary;
        }

        private static string Step(List<ScheduleRollingSeries> points)
        {
            if (points.Count < 2)
                return "n/a";

            int step = points[1].Minute - points[0].Minute;

            return step > 0
                ? step.ToString(CultureInfo.InvariantCulture)
                : "irregular";
        }

        private static List<string> BuildData(List<ScheduleRollingSeries> series)
        {
            List<string> rows = new List<string> { "series,minute,value" };

            List<ScheduleRollingSeries> points = series
                .Where(s => s != null)
                .OrderBy(s => s.Minute)
                .ToList();

            foreach (ScheduleRollingSeries point in points)
            {
                rows.Add(ChartText.Row(
                    "Domestic departures",
                    point.Minute.ToString(),
                    point.DomesticDeparture.ToString()));

                rows.Add(ChartText.Row(
                    "International departures",
                    point.Minute.ToString(),
                    point.InternationalDeparture.ToString()));

                rows.Add(ChartText.Row(
                    "Combined",
                    point.Minute.ToString(),
                    point.Combined.ToString()));
            }

            return rows;
        }

        private static int Peak(
            List<ScheduleRollingSeries> series,
            Func<ScheduleRollingSeries, int> value)
        {
            return series.Count == 0 ? 0 : series.Max(value);
        }

        private static string PeakTime(
            List<ScheduleRollingSeries> series,
            Func<ScheduleRollingSeries, int> value)
        {
            if (series.Count == 0)
                return "n/a";

            int minute = series
                .OrderByDescending(value)
                .First()
                .Minute;

            return SchedulePassengerGenerationResult.FormatMinute(minute);
        }

        protected override Bitmap Icon => null;

        public override Guid ComponentGuid =>
            new Guid("D6A1F4C2-8E57-4B93-A10D-7F2C5B8E9A34");
    }
}
