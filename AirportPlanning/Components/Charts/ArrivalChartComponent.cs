using AirportPlanning.Core.Charts;
using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Inputs;
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
    /// Plots generated demand. Three views:
    ///   Demand Profile - per-minute arrivals across the window;
    ///   By Type       - the same, split by passenger type;
    ///   Show-Up Curve - the Leisure / Business / Mix curves themselves, so the
    ///                  input shapes can be compared directly.
    ///
    /// Each view also publishes what it plotted, so the image can be checked
    /// against the underlying values rather than read as a standalone picture.
    /// </summary>
    public class ArrivalChartComponent : GH_Component
    {
        public ArrivalChartComponent()
          : base("Arrival Chart", "ArrChart",
            "Plots generated demand: per-minute arrivals across the analysis window, splits by passenger type, or the show-up curves themselves.",
            "Airport Planning", "Charts")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Arrival Simulations", "AS",
                "Arrival Simulations from the Windowed Passenger Generator or Schedule Passenger Generator.",
                GH_ParamAccess.list);
            pManager[0].Optional = true;

            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "Passengers from the generator. Required for the by-type view.",
                GH_ParamAccess.list);
            pManager[1].Optional = true;

            pManager.AddGenericParameter(
                "Show-Up Profiles", "SUP",
                "Show-Up Profiles to compare, from the Show-Up Profile component. Required for the Show-Up Curve view.",
                GH_ParamAccess.list);
            pManager[2].Optional = true;

            pManager.AddTextParameter(
                "Title", "T",
                "Chart title. Leave empty for the default.",
                GH_ParamAccess.item, "");
            pManager[3].Optional = true;

            pManager.AddTextParameter(
                "View", "V",
                "Demand Profile, By Type, or Show-Up Curve.",
                GH_ParamAccess.item, "Demand Profile");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Chart", "C",
                "Rendered chart bitmap.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Summary", "S",
                "What the chart shows: view, source population, window and peak values.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Chart Data", "D",
                "CSV rows (series,minute,value) for the values actually plotted.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<ArrivalSimulationResult> simulations = new List<ArrivalSimulationResult>();
            List<Passenger> passengers = new List<Passenger>();
            List<ShowUpProfile> showUps = new List<ShowUpProfile>();
            string title = "";
            string view = "Demand Profile";

            DA.GetDataList(0, simulations);
            DA.GetDataList(1, passengers);
            DA.GetDataList(2, showUps);
            DA.GetData(3, ref title);
            DA.GetData(4, ref view);

            string mode = (view ?? "").Trim().ToLowerInvariant();

            PlotModel model;
            List<string> summary;
            List<string> data;

            if (mode == "show-up curve")
            {
                if (showUps == null || showUps.Count == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        "The Show-Up Curve view needs at least one Show-Up Profile input.");
                    return;
                }

                model = ChartRenderer.CreateShowUpCurveModel(
                    showUps,
                    string.IsNullOrWhiteSpace(title) ? "Show-Up Curves by Passenger Mix" : title);

                summary = BuildShowUpSummary(showUps);
                data = BuildShowUpData(showUps);
            }
            else if (mode == "by type")
            {
                if (passengers == null || passengers.Count == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        "The By Type view needs a Passengers input.");
                    return;
                }

                model = ChartRenderer.CreateArrivalProfileModel(
                    passengers,
                    string.IsNullOrWhiteSpace(title) ? "Arrivals by Passenger Type" : title);

                summary = BuildByTypeSummary(passengers);
                data = BuildByTypeData(passengers);
            }
            else
            {
                if (simulations == null || simulations.Count == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        "No arrival simulations received. Connect a passenger generator's Arrival Simulations output.");
                    return;
                }

                model = ChartRenderer.CreateArrivalDemandModel(
                    simulations,
                    string.IsNullOrWhiteSpace(title) ? "Arrival Demand Profile" : title);

                summary = BuildDemandSummary(simulations);
                data = BuildDemandData(simulations);
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
            DA.SetDataList(1, summary);
            DA.SetDataList(2, data);
        }

        private static SortedDictionary<int, int> AggregateTotals(
            IEnumerable<ArrivalSimulationResult> simulations)
        {
            SortedDictionary<int, int> totals = new SortedDictionary<int, int>();

            foreach (ArrivalSimulationResult simulation in simulations)
            {
                if (simulation?.Histogram == null)
                    continue;

                int start = (int)Math.Round(simulation.StepStartMinute);

                for (int bucket = 0; bucket < simulation.Histogram.Count; bucket++)
                {
                    int minute = start + bucket;

                    totals.TryGetValue(minute, out int running);
                    totals[minute] = running + simulation.Histogram[bucket];
                }
            }

            return totals;
        }

        private static List<string> BuildDemandSummary(
            List<ArrivalSimulationResult> simulations)
        {
            SortedDictionary<int, int> totals = AggregateTotals(simulations);

            int peakMinute = 0;
            int peakValue = 0;

            foreach (KeyValuePair<int, int> entry in totals)
            {
                if (entry.Value > peakValue)
                {
                    peakValue = entry.Value;
                    peakMinute = entry.Key;
                }
            }

            int warmUp = simulations.Count(s => s != null && s.IsWarmUp);
            int design = simulations.Count(s => s != null && s.IsDesignStep);

            return new List<string>
            {
                "View: demand profile (per minute, all simulations summed)",
                $"Simulations: {simulations.Count}; warm-up {warmUp}; design-step {design}",
                totals.Count > 0
                    ? $"Window: minute {totals.Keys.First()}-{totals.Keys.Last()}; {totals.Values.Sum()} passengers"
                    : "Window: no arrival buckets",
                totals.Count > 0
                    ? $"Peak: {peakValue} pax/min at minute {peakMinute}"
                    : "Peak: none",
                "Note: warm-up and design-hour spans are shaded on the chart."
            };
        }

        private static List<string> BuildDemandData(
            List<ArrivalSimulationResult> simulations)
        {
            List<string> rows = new List<string> { "series,minute,value" };

            foreach (KeyValuePair<int, int> entry in AggregateTotals(simulations))
            {
                rows.Add(ChartText.Row(
                    "total",
                    entry.Key.ToString(),
                    entry.Value.ToString()));
            }

            return rows;
        }

        private static List<string> BuildByTypeSummary(List<Passenger> passengers)
        {
            List<Passenger> windowed = passengers
                .Where(p => p != null && !p.IsWarmUp)
                .ToList();

            List<string> summary = new List<string>
            {
                "View: arrivals by passenger type",
                $"Passengers: {windowed.Count}; warm-up excluded"
            };

            foreach (IGrouping<PassengerType, Passenger> group in windowed
                .GroupBy(p => p.PassengerType)
                .OrderBy(g => g.Key.ToString()))
            {
                summary.Add($"{group.Key}: {group.Count()} passengers");
            }

            return summary;
        }

        private static List<string> BuildByTypeData(List<Passenger> passengers)
        {
            List<string> rows = new List<string> { "series,minute,value" };

            Dictionary<string, Dictionary<int, int>> byType =
                new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);

            foreach (Passenger passenger in passengers)
            {
                if (passenger == null || passenger.IsWarmUp)
                    continue;

                string key = passenger.PassengerType.ToString();

                if (!byType.TryGetValue(key, out Dictionary<int, int> buckets))
                {
                    buckets = new Dictionary<int, int>();
                    byType[key] = buckets;
                }

                int minute = (int)Math.Floor(passenger.ArrivalTime);

                buckets.TryGetValue(minute, out int running);
                buckets[minute] = running + 1;
            }

            foreach (KeyValuePair<string, Dictionary<int, int>> entry in byType
                .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                foreach (KeyValuePair<int, int> bucket in entry.Value.OrderBy(b => b.Key))
                {
                    rows.Add(ChartText.Row(
                        entry.Key,
                        bucket.Key.ToString(),
                        bucket.Value.ToString()));
                }
            }

            return rows;
        }

        private static List<string> BuildShowUpSummary(List<ShowUpProfile> profiles)
        {
            List<string> summary = new List<string>
            {
                "View: show-up curves (share of the hour arrived)",
                $"Profiles: {profiles.Count}"
            };

            foreach (ShowUpProfile profile in profiles)
            {
                if (profile == null)
                    continue;

                summary.Add(
                    $"{profile.Name}: W15 {profile.W15:P1}; W30 {profile.W30:P1}; W60 {profile.W60:P1}");
            }

            return summary;
        }

        private static List<string> BuildShowUpData(List<ShowUpProfile> profiles)
        {
            List<string> rows = new List<string>
            {
                "series,minutesBeforeDeparture,cumulativeShare"
            };

            foreach (ShowUpProfile profile in profiles)
            {
                if (profile == null)
                    continue;

                for (int band = 0; band <= ShowUpProfile.ChunkCount; band++)
                {
                    double minutesOut =
                        ShowUpProfile.BlockMinutes -
                        band * (ShowUpProfile.BlockMinutes / ShowUpProfile.ChunkCount);

                    double cumulative =
                        profile.GetCumulativeAt(band * (ShowUpProfile.BlockMinutes / ShowUpProfile.ChunkCount));

                    rows.Add(ChartText.Row(
                        profile.Name,
                        ChartText.Number(minutesOut),
                        ChartText.Number(cumulative)));
                }
            }

            return rows;
        }

        protected override Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("B2C3D4E5-F6A7-8901-BCDE-F12345678901"); }
        }
    }
}
