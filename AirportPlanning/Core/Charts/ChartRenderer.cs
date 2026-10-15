using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
#if HAS_WINFORMS_EXPORTER
using OxyPlot.WindowsForms;
#endif
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AirportPlanning.Core.Charts
{
    /// <summary>
    /// Builds and renders the plots for the queuing model.
    ///
    /// OxyPlot 2.x dropped <c>PlotModel.LegendPosition</c>; a legend is now a
    /// child element added to <c>PlotModel.Legends</c>, which is what
    /// <see cref="AddLegend"/> does. Rendering goes through
    /// <see cref="PngExporter"/> so nothing but a <see cref="Bitmap"/> ever
    /// reaches Grasshopper - no PlotView control is involved.
    ///
    /// Kept free of Grasshopper types so the plots stay testable.
    /// </summary>
    public static class ChartRenderer
    {
        /// <summary>Palette used to keep series distinguishable.</summary>
        private static readonly OxyColor[] Palette =
        {
            OxyColors.SteelBlue,
            OxyColors.OrangeRed,
            OxyColors.SeaGreen,
            OxyColors.MediumPurple,
            OxyColors.DarkGoldenrod,
            OxyColors.Crimson,
            OxyColors.Teal,
            OxyColors.SaddleBrown,
            OxyColors.SlateGray,
            OxyColors.OliveDrab
        };

        /// <summary>
        /// True when this build can actually rasterize a plot. False on the
        /// Rhino 8 macOS target, where the only available OxyPlot renderer is
        /// Windows-only; plot models still build there, they just cannot be
        /// turned into a bitmap.
        /// </summary>
        public static bool CanRender
        {
            get
            {
#if HAS_WINFORMS_EXPORTER
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Renders a plot model to a bitmap. Returns null when the model cannot
        /// be rendered so callers never propagate a half-built image.
        /// </summary>
        public static Bitmap ExportToBitmap(
            PlotModel model,
            int width = 900,
            int height = 550)
        {
            if (model == null)
                return null;

#if HAS_WINFORMS_EXPORTER
            try
            {
                PngExporter exporter = new PngExporter
                {
                    Width = width,
                    Height = height
                };

                return exporter.ExportToBitmap(model);
            }
            catch (Exception)
            {
                return null;
            }
#else
            return null;
#endif
        }

        /// <summary>
        /// Adds a legend as a child element. OxyPlot 2.x requires this instead
        /// of setting position properties on the plot model.
        /// </summary>
        private static void AddLegend(PlotModel model)
        {
            model.Legends.Add(new Legend
            {
                LegendPosition = LegendPosition.RightTop,
                LegendPlacement = LegendPlacement.Outside
            });
        }

        /// <summary>
        /// Wait95 against physical server count for a single pool, with the
        /// target drawn as a reference line and the chosen capacity called out.
        ///
        /// Only the points the search actually sampled are plotted. That is a
        /// sparse logarithmic sample of the capacity axis, so the line is drawn
        /// as a guide to eye rather than a claim about the true curve between
        /// samples.
        /// </summary>
        public static PlotModel CreateOptimizerSearchModel(
            List<SearchTracePoint> trace,
            double targetWait95,
            string title = "Optimizer Search: Wait95 vs Server Count")
        {
            PlotModel model = NewPlot(title, "Physical servers", "Wait95 (minutes)");

            List<SearchTracePoint> samples = Normalize(trace);

            if (samples.Count == 0)
                return model;

            int minCapacity = samples.Min(p => p.Capacity);
            int maxCapacity = samples.Max(p => p.Capacity);

            LineSeries curve = new LineSeries
            {
                Title = "Wait95 (sampled)",
                Color = Palette[0],
                StrokeThickness = 2,
                MarkerType = MarkerType.Circle,
                MarkerSize = 5,
                MarkerFill = OxyColors.White,
                MarkerStroke = Palette[0],
                MarkerStrokeThickness = 1.5,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = "servers: {2:0}\nWait95: {Y:0.00} min"
            };

            foreach (SearchTracePoint point in samples)
                curve.Points.Add(new DataPoint(point.Capacity, point.Wait95));

            model.Series.Add(curve);

            // The search returns the smallest capacity that met the target; that
            // is the number the design is actually sized on, so it is called out
            // separately from the samples around it.
            SearchTracePoint chosen = samples
                .OrderBy(p => p.Capacity)
                .LastOrDefault(p => targetWait95 > 0.0 ? p.Wait95 <= targetWait95 : true);

            if (targetWait95 > 0.0 && samples.Count > 1)
            {
                model.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Horizontal,
                    Y = targetWait95,
                    Color = Palette[1],
                    LineStyle = LineStyle.Dash,
                    StrokeThickness = 1.75,
                    Text = $"target {targetWait95:0.##} min",
                    TextColor = Palette[1],
                    TextPosition = new DataPoint(minCapacity, targetWait95),
                    TextVerticalAlignment = VerticalAlignment.Top,
                    TextHorizontalAlignment = HorizontalAlignment.Left
                });
            }

            if (chosen != null)
            {
                ScatterSeries marker = new ScatterSeries
                {
                    Title = $"selected ({chosen.Capacity} servers)",
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 12,
                    MarkerFill = OxyColors.Transparent,
                    MarkerStroke = Palette[2],
                    MarkerStrokeThickness = 2.5
                };

                marker.Points.Add(new ScatterPoint(
                    chosen.Capacity, chosen.Wait95, 12, 0.0, null));

                model.Series.Add(marker);
            }

            AddLegend(model);

            return model;
        }

        /// <summary>
        /// Wait95 against server count for every pool of a facility, one line per
        /// channel. Each pool keeps its own target line so channels sized to
        /// different service levels stay readable.
        /// </summary>
        public static PlotModel CreateMultiPoolOptimizerModel(
            IEnumerable<ResourceSearchOutcome> outcomes,
            string title = "Optimizer Search by Channel")
        {
            PlotModel model = NewPlot(title, "Physical servers", "Wait95 (minutes)");

            List<ResourceSearchOutcome> pools = Normalize(outcomes);

            if (pools.Count == 0)
                return model;

            int index = 0;

            foreach (ResourceSearchOutcome outcome in pools)
            {
                List<SearchTracePoint> samples = Normalize(outcome.SearchTrace);

                if (samples.Count == 0)
                    continue;

                OxyColor color = Palette[index % Palette.Length];

                string name = outcome.Plan != null && !outcome.Plan.IsSharedPool
                    ? outcome.Plan.Channel
                    : "Shared";

                LineSeries curve = new LineSeries
                {
                    Title = name,
                    Color = color,
                    StrokeThickness = 2,
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 4,
                    MarkerFill = OxyColors.White,
                    MarkerStroke = color,
                    MarkerStrokeThickness = 1.5,
                    CanTrackerInterpolatePoints = false,
                    TrackerFormatString = $"{name}\nservers: {{2:0}}\nWait95: {{Y:0.00}} min"
                };

                foreach (SearchTracePoint point in samples)
                    curve.Points.Add(new DataPoint(point.Capacity, point.Wait95));

                model.Series.Add(curve);

                double target = outcome.Plan != null ? outcome.Plan.TargetWait95 : 0.0;

                if (target > 0.0)
                {
                    model.Annotations.Add(new LineAnnotation
                    {
                        Type = LineAnnotationType.Horizontal,
                        Y = target,
                        Color = OxyColor.FromAColor(70, color),
                        LineStyle = LineStyle.Dot,
                        StrokeThickness = 1.25,
                        Text = $"{name} target {target:0.##}",
                        TextColor = color,
                        TextPosition = new DataPoint(samples.Min(p => p.Capacity), target),
                        TextVerticalAlignment = VerticalAlignment.Top,
                        TextHorizontalAlignment = HorizontalAlignment.Left
                    });
                }

                index++;
            }

            AddLegend(model);

            return model;
        }

        /// <summary>
        /// Per-minute arrival counts across the whole analysis window, drawn as
        /// a continuous demand curve with the warm-up and design-hour spans
        /// shaded behind it.
        ///
        /// A curve rather than per-minute bars: a five-hour window is ~300
        /// minutes, which leaves bars a couple of pixels wide. (OxyPlot's
        /// BarSeries also forces a CategoryAxis on Y, which would have replaced
        /// the numeric minute axis with evenly spaced labels.)
        /// </summary>
        public static PlotModel CreateArrivalDemandModel(
            IEnumerable<ArrivalSimulationResult> simulations,
            string title = "Arrival Demand Profile")
        {
            PlotModel model = NewPlot(title, "Minute of analysis window", "Passengers per minute");

            List<ArrivalSimulationResult> sources = Normalize(simulations);

            if (sources.Count == 0)
                return model;

            Dictionary<int, int> totals = new Dictionary<int, int>();
            List<int> designMinutes = new List<int>();
            List<int> warmUpMinutes = new List<int>();

            foreach (ArrivalSimulationResult simulation in sources)
            {
                if (simulation == null || simulation.Histogram == null)
                    continue;

                int start = (int)Math.Round(simulation.StepStartMinute);

                for (int bucket = 0; bucket < simulation.Histogram.Count; bucket++)
                {
                    int minute = start + bucket;

                    totals.TryGetValue(minute, out int running);
                    totals[minute] = running + simulation.Histogram[bucket];

                    if (simulation.IsDesignStep)
                        designMinutes.Add(minute);

                    if (simulation.IsWarmUp)
                        warmUpMinutes.Add(minute);
                }
            }

            if (totals.Count == 0)
                return model;

            List<int> minutes = totals.Keys.OrderBy(m => m).ToList();

            int peakMinute = minutes[0];
            int peakValue = int.MinValue;

            foreach (int minute in minutes)
            {
                if (totals[minute] > peakValue)
                {
                    peakValue = totals[minute];
                    peakMinute = minute;
                }
            }

            double bandTop = peakValue > 0 ? peakValue : 1.0;

            AddSpanBand(model, warmUpMinutes, OxyColor.FromAColor(38, Palette[7]), "Warm-up", bandTop);
            AddSpanBand(model, designMinutes, OxyColor.FromAColor(38, Palette[1]), "Design hour", bandTop);

            LineSeries demand = new LineSeries
            {
                Title = "Arrivals per minute",
                Color = Palette[0],
                StrokeThickness = 2,
                MarkerType = MarkerType.None,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = "minute {2:0}\n{Y:0} pax/min"
            };

            foreach (int minute in minutes)
                demand.Points.Add(new DataPoint(minute, totals[minute]));

            model.Series.Add(demand);

            // Call out the busiest minute: it is what the facility has to be
            // sized for, and it is usually not the middle of the design step.
            ScatterSeries peak = new ScatterSeries
            {
                Title = $"peak {peakValue} pax/min at minute {peakMinute}",
                MarkerType = MarkerType.Circle,
                MarkerSize = 9,
                MarkerFill = Palette[1],
                MarkerStroke = OxyColors.White,
                MarkerStrokeThickness = 1.5
            };

            peak.Points.Add(new ScatterPoint(peakMinute, peakValue, 9, 0.0, null));
            model.Series.Add(peak);

            AddLegend(model);

            return model;
        }

        /// <summary>
        /// Shades the span a list of minutes covers, labelled in place. Bands sit
        /// in the BelowSeries layer so the demand curve still draws over them,
        /// and are sized to the full data range so they read as vertical regions.
        /// </summary>
        private static void AddSpanBand(
            PlotModel model,
            List<int> span,
            OxyColor fill,
            string label,
            double maxY)
        {
            if (span == null || span.Count == 0)
                return;

            model.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = span.Min(),
                MaximumX = span.Max(),
                MinimumY = 0.0,
                MaximumY = maxY,
                Fill = fill,
                Stroke = OxyColors.Transparent,
                StrokeThickness = 0.0,
                Layer = AnnotationLayer.BelowSeries,
                ClipByYAxis = false,
                Text = label,
                TextColor = OxyColor.FromAColor(190, OxyColors.Black),
                TextHorizontalAlignment = HorizontalAlignment.Center,
                TextVerticalAlignment = VerticalAlignment.Top
            });
        }

        /// <summary>
        /// Arrivals stacked by passenger type across the window, which is the
        /// view used to sanity-check the domestic/international split.
        /// </summary>
        public static PlotModel CreateArrivalProfileModel(
            List<Passenger> passengers,
            string title = "Arrivals by Passenger Type")
        {
            PlotModel model = NewPlot(title, "Minute of analysis window", "Passengers per minute");

            if (passengers == null || passengers.Count == 0)
                return model;

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

            int index = 0;

            foreach (KeyValuePair<string, Dictionary<int, int>> entry in byType)
            {
                List<int> minutes = entry.Value.Keys.OrderBy(m => m).ToList();

                if (minutes.Count == 0)
                    continue;

                LineSeries series = new LineSeries
                {
                    Title = entry.Key,
                    Color = Palette[index % Palette.Length],
                    StrokeThickness = 2,
                    MarkerType = MarkerType.None,
                    CanTrackerInterpolatePoints = false,
                    TrackerFormatString = $"{entry.Key}\nminute: {{2:0}}\narrivals: {{Y:0}}"
                };

                foreach (int minute in minutes)
                    series.Points.Add(new DataPoint(minute, entry.Value[minute]));

                model.Series.Add(series);
                index++;
            }

            AddLegend(model);

            return model;
        }

        /// <summary>
        /// The cumulative arrival / departure diagram: cumulative passengers
        /// against cumulative passengers-served, with the gap between them
        /// shaded.
        ///
        /// This is the standard airport capacity diagram, and it is the clearest
        /// way to show what the queuing model does. Two rising lines, one gap:
        ///   gap opens  -> arrivals are outrunning the counters
        ///   gap widest -> busiest moment
        ///   gap closes -> the system is catching up
        /// and the slope of the departure line shows how hard the counters are
        /// working.
        ///
        /// Note the gap counts everyone still in the system - waiting *plus* being
        /// served - because "departure" here means service complete. It is
        /// therefore a little larger than <see cref="QueueResults.PeakQueueLength"/>,
        /// which counts only the waiting. Use the queue-over-time view for the
        /// waiting figure; the two are deliberately not conflated.
        ///
        /// The service-capacity line is what the counters could have done had
        /// demand been unlimited: crossing it means the facility is past its
        /// theoretical throughput, which is usually where the queue starts to run
        /// away.
        /// </summary>
        public static PlotModel CreateCumulativeFlowModel(
            QueueResults results,
            string title = "Queuing Model: Cumulative Arrivals vs Departures")
        {
            PlotModel model = NewPlot(title, "Minute of analysis window", "Cumulative passengers");

            List<QueueCumulativePoint> flow = GetFlow(results);

            if (flow.Count == 0)
                return model;

            int activeCount = GetActiveServerCount(results);

            double start = flow[0].Time;

            ShadeInSystemGap(model, flow);

            LineSeries arrivals = new LineSeries
            {
                Title = "Cumulative arrivals",
                Color = Palette[0],
                StrokeThickness = 2.5,
                MarkerType = MarkerType.None,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = "minute {2:0}\narrived {Y:0}"
            };

            LineSeries departures = new LineSeries
            {
                Title = "Cumulative departures (service complete)",
                Color = Palette[1],
                StrokeThickness = 2.5,
                MarkerType = MarkerType.None,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = "minute {2:0}\nserved {Y:0}"
            };

            foreach (QueueCumulativePoint point in flow)
            {
                arrivals.Points.Add(new DataPoint(point.Time, point.CumulativeArrivals));
                departures.Points.Add(new DataPoint(point.Time, point.CumulativeDepartures));
            }

            model.Series.Add(arrivals);
            model.Series.Add(departures);

            if (activeCount > 0)
            {
                // Reference only: the same slope as a fully saturated facility.
                LineSeries capacity = new LineSeries
                {
                    Title = $"Service capacity ({activeCount} staffed)",
                    Color = Palette[7],
                    StrokeThickness = 1.5,
                    LineStyle = LineStyle.Dash,
                    MarkerType = MarkerType.None,
                    CanTrackerInterpolatePoints = false,
                    TrackerFormatString = "minute {2:0}\ncapacity {Y:0}"
                };

                double end = flow[flow.Count - 1].Time;
                double slope = activeCount / 60.0;

                capacity.Points.Add(new DataPoint(start, 0.0));
                capacity.Points.Add(new DataPoint(end, (end - start) * slope));

                model.Series.Add(capacity);
            }

            // Annotated from the plotted data, not from QueueResults.PeakQueueLength.
            // The gap here counts everyone in the system - waiting plus being
            // served - whereas PeakQueueLength counts only the waiting. Borrowing
            // the reported figure would put a number on the chart that disagrees
            // with the curve underneath it.
            AnnotateWidestGap(model, flow);

            AddLegend(model);

            return model;
        }

        /// <summary>
        /// Passengers waiting against time: the queue building and draining, with
        /// the design-hour span shaded behind it.
        ///
        /// This plots <see cref="QueueResults.QueueTimeline"/>, which clears a
        /// passenger the moment their service starts. The cumulative diagram's
        /// gap is a different, larger quantity (it waits for service to finish),
        /// so reusing it here would overstate the queue by the number of
        /// passengers currently at a counter.
        /// </summary>
        public static PlotModel CreateQueueTimelineModel(
            QueueResults results,
            string title = "Queuing Model: Queue Length Over Time")
        {
            PlotModel model = NewPlot(title, "Minute of analysis window", "Passengers waiting");

            List<QueueTimelinePoint> timeline = GetTimeline(results);

            if (timeline.Count == 0)
                return model;

            ShadeDesignStep(model, results);

            LineSeries queue = new LineSeries
            {
                Title = "Passengers waiting",
                Color = Palette[0],
                StrokeThickness = 2.5,
                MarkerType = MarkerType.None,
                CanTrackerInterpolatePoints = false,
                TrackerFormatString = "minute {2:0}\nwaiting {Y:0}"
            };

            // The timeline only carries events where somebody queued, so it can
            // begin above zero. Anchoring it to the start of the reported window
            // shows the queue building from empty, which is the honest reading.
            double windowStart = timeline[0].Time;

            List<QueueCumulativePoint> flow = GetFlow(results);

            if (flow.Count > 0)
                windowStart = Math.Min(windowStart, flow[0].Time);

            if (timeline[0].Queue > 0 || timeline[0].Time > windowStart)
                queue.Points.Add(new DataPoint(windowStart, 0.0));

            foreach (QueueTimelinePoint point in timeline)
                queue.Points.Add(new DataPoint(point.Time, point.Queue));

            model.Series.Add(queue);

            if (results.PeakQueueLength > 0)
            {
                ScatterSeries peak = new ScatterSeries
                {
                    Title = $"peak queue {results.PeakQueueLength} waiting",
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 9,
                    MarkerFill = Palette[1],
                    MarkerStroke = OxyColors.White,
                    MarkerStrokeThickness = 1.5
                };

                peak.Points.Add(new ScatterPoint(
                    results.PeakQueueTime, results.PeakQueueLength, 9, 0.0, null));

                model.Series.Add(peak);
            }

            AnnotatePeakQueue(model, results);

            AddLegend(model);

            return model;
        }

        private static List<QueueTimelinePoint> GetTimeline(QueueResults results)
        {
            if (results == null || results.QueueTimeline == null)
                return new List<QueueTimelinePoint>();

            return results.QueueTimeline
                .Where(p => p != null)
                .OrderBy(p => p.Time)
                .ToList();
        }

        private static List<QueueCumulativePoint> GetFlow(QueueResults results)
        {
            if (results == null || results.CumulativeFlow == null)
                return new List<QueueCumulativePoint>();

            return results.CumulativeFlow
                .Where(p => p != null)
                .OrderBy(p => p.Time)
                .ToList();
        }

        /// <summary>
        /// Number of servers actually simulated. Read from the results rather
        /// than taken as a parameter because a multi-channel solve merges its
        /// pools and only assigns Resources after the metrics were calculated,
        /// so the calculator never saw a reliable count.
        /// </summary>
        private static int GetActiveServerCount(QueueResults results)
        {
            if (results == null || results.Resources == null)
                return 0;

            int count = results.Resources.Count;

            return count < 0 ? 0 : count;
        }

        /// <summary>
        /// Shades the stretches where the gap between the cumulative curves is
        /// non-empty, i.e. where somebody is in the system. Runs are emitted
        /// rather than a band per sample: a minute grid over a long window would
        /// otherwise add hundreds of annotations, and a busy period normally has
        /// only a handful of distinct excursions above zero.
        /// </summary>
        private static void ShadeInSystemGap(
            PlotModel model,
            List<QueueCumulativePoint> flow)
        {
            int peak = 0;

            foreach (QueueCumulativePoint point in flow)
                peak = Math.Max(peak, point.QueueLength);

            if (peak <= 0)
                return;

            double top = peak * 1.08;

            int runStart = -1;

            for (int i = 0; i < flow.Count; i++)
            {
                bool queued = flow[i].QueueLength > 0;

                if (queued && runStart < 0)
                    runStart = i;

                bool last = i == flow.Count - 1;

                if (runStart >= 0 && (!queued || last))
                {
                    int runEnd = queued ? i : i - 1;

                    if (runEnd > runStart)
                    {
                        AddRect(model, flow[runStart].Time, flow[runEnd].Time, top,
                            OxyColor.FromAColor(26, OxyColors.Crimson));
                    }

                    runStart = -1;
                }
            }
        }

        /// <summary>
        /// Marks the widest gap in the plotted series, using the sampled data
        /// rather than the reported metrics, so the number on the chart always
        /// matches the curves underneath it.
        /// </summary>
        private static void AnnotateWidestGap(
            PlotModel model,
            List<QueueCumulativePoint> flow)
        {
            QueueCumulativePoint widest = null;

            foreach (QueueCumulativePoint point in flow)
            {
                if (widest == null || point.QueueLength > widest.QueueLength)
                    widest = point;
            }

            if (widest == null || widest.QueueLength <= 0)
                return;

            model.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = widest.Time,
                Color = OxyColor.FromAColor(120, Palette[1]),
                LineStyle = LineStyle.Dot,
                StrokeThickness = 1.5,
                Text = $"widest gap {widest.QueueLength} pax in system at {widest.Time:0} min",
                TextColor = Palette[1],
                TextPosition = new DataPoint(widest.Time, 0.0),
                TextVerticalAlignment = VerticalAlignment.Top,
                TextHorizontalAlignment = HorizontalAlignment.Left
            });
        }

        /// <summary>
        /// Shades the design-hour step behind the queue, taken from the
        /// per-step metrics the solver already publishes.
        /// </summary>
        private static void ShadeDesignStep(
            PlotModel model,
            QueueResults results)
        {
            if (results == null ||
                results.DesignStepIndex < 0 ||
                results.IntervalMetrics == null)
                return;

            foreach (IntervalQueueMetrics metric in results.IntervalMetrics)
            {
                if (metric == null || metric.StepIndex != results.DesignStepIndex)
                    continue;

                AddRect(model, metric.StepStartMinute, metric.StepEndMinute, 0.0,
                    OxyColor.FromAColor(30, Palette[1]));

                return;
            }
        }

        private static void AddRect(
            PlotModel model,
            double minX,
            double maxX,
            double maxY,
            OxyColor fill)
        {
            model.Annotations.Add(new RectangleAnnotation
            {
                MinimumX = minX,
                MaximumX = maxX,

                // A maxY of zero means "stretch to the top of the plot", which is
                // what a shaded background span wants; the queue chart sizes its
                // own band and passes an explicit height.
                MaximumY = maxY > 0.0 ? maxY : double.NaN,

                MinimumY = 0.0,
                Fill = fill,
                Stroke = OxyColors.Transparent,
                StrokeThickness = 0.0,
                Layer = AnnotationLayer.BelowSeries,
                ClipByYAxis = false
            });
        }

        /// <summary>
        /// Marks the peak the solver already reported, so the chart and the
        /// numeric outputs can never disagree.
        /// </summary>
        private static void AnnotatePeakQueue(PlotModel model, QueueResults results)
        {
            if (results == null || results.PeakQueueLength <= 0)
                return;

            model.Annotations.Add(new LineAnnotation
            {
                Type = LineAnnotationType.Vertical,
                X = results.PeakQueueTime,
                Color = OxyColor.FromAColor(120, Palette[1]),
                LineStyle = LineStyle.Dot,
                StrokeThickness = 1.5,
                Text = $"peak queue {results.PeakQueueLength} pax at {results.PeakQueueTime:0} min",
                TextColor = Palette[1],
                TextPosition = new DataPoint(results.PeakQueueTime, 0.0),
                TextVerticalAlignment = VerticalAlignment.Top,
                TextHorizontalAlignment = HorizontalAlignment.Left
            });
        }

        /// <summary>
        /// The show-up profiles themselves: share of the hour arrived, against
        /// minutes before departure, one line per passenger type.
        ///
        /// X runs backwards on purpose - 60 minutes before departure down to 0 -
        /// because that is how a planner reads a show-up curve, and it puts the
        /// busiest part of each curve next to departure on the right.
        /// </summary>
        public static PlotModel CreateShowUpCurveModel(
            IEnumerable<ShowUpProfile> profiles,
            string title = "Show-Up Curves by Passenger Mix")
        {
            PlotModel model = NewPlot(
                title, "Minutes before departure", "Share of hour arrived");

            List<ShowUpProfile> sources = Normalize(profiles);

            if (sources.Count == 0)
                return model;

            int index = 0;

            foreach (ShowUpProfile curve in sources)
            {
                LineSeries series = new LineSeries
                {
                    Title = curve.Name,
                    Color = Palette[index % Palette.Length],
                    StrokeThickness = 2.5,
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 4,
                    MarkerFill = OxyColors.White,
                    MarkerStroke = Palette[index % Palette.Length],
                    MarkerStrokeThickness = 1.5,
                    CanTrackerInterpolatePoints = false,
                    TrackerFormatString =
                        $"{curve.Name}\n{{2:0}} min before departure\narrived: {{Y:P1}}"
                };

                // One point on every band boundary plus the hour itself, so the
                // markers land exactly where the ten shares change value.
                for (int band = 0; band <= ShowUpProfile.ChunkCount; band++)
                {
                    double minutesOut =
                        ShowUpProfile.BlockMinutes -
                        band * (ShowUpProfile.BlockMinutes / ShowUpProfile.ChunkCount);

                    series.Points.Add(
                        new DataPoint(minutesOut, curve.GetCumulativeAt(band * (ShowUpProfile.BlockMinutes / ShowUpProfile.ChunkCount))));
                }

                model.Series.Add(series);

                // Call out the nested window shares a planner would quote. The
                // labels are staggered along the axis because the three shipped
                // W15 values sit close together and would otherwise overprint.
                model.Annotations.Add(new LineAnnotation
                {
                    Type = LineAnnotationType.Horizontal,
                    Y = curve.W15,
                    Color = OxyColor.FromAColor(90, Palette[index % Palette.Length]),
                    LineStyle = LineStyle.Dot,
                    StrokeThickness = 1.25,
                    Text = $"{curve.Name} W15 {curve.W15:P0}",
                    TextColor = Palette[index % Palette.Length],
                    TextPosition = new DataPoint(index * 16.0, curve.W15),
                    TextVerticalAlignment = VerticalAlignment.Bottom,
                    TextHorizontalAlignment = HorizontalAlignment.Left
                });

                index++;
            }

            AddLegend(model);

            return model;
        }

        private static List<ShowUpProfile> Normalize(IEnumerable<ShowUpProfile> profiles)
        {
            if (profiles == null)
                return new List<ShowUpProfile>();

            return profiles.Where(p => p != null).ToList();
        }

        private static PlotModel NewPlot(string title, string xTitle, string yTitle)
        {
            PlotModel model = new PlotModel
            {
                Title = title,
                Background = OxyColors.White,
                PlotAreaBackground = OxyColor.FromAColor(20, OxyColors.Black),
                PlotMargins = new OxyThickness(60, 30, 20, 40),
                Padding = new OxyThickness(8)
            };

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = xTitle,
                MinimumPadding = 0.02,
                MaximumPadding = 0.02,
                MajorGridlineStyle = LineStyle.None,
                MinorGridlineStyle = LineStyle.None,
                AxislineStyle = LineStyle.Solid
            });

            model.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = yTitle,
                MinimumPadding = 0.05,
                MajorGridlineStyle = LineStyle.Dot,
                MinorGridlineStyle = LineStyle.None,
                AxislineStyle = LineStyle.Solid
            });

            return model;
        }

        /// <summary>
        /// Drops unusable points so one bad sample cannot break a whole chart.
        /// Duplicate capacities are collapsed, keeping the last measurement,
        /// since the refinement step re-visits capacities.
        /// </summary>
        private static List<SearchTracePoint> Normalize(List<SearchTracePoint> trace)
        {
            if (trace == null || trace.Count == 0)
                return new List<SearchTracePoint>();

            return trace
                .Where(p => p != null && p.Capacity >= 0 && !double.IsNaN(p.Wait95))
                .GroupBy(p => p.Capacity)
                .Select(g => g.Last())
                .OrderBy(p => p.Capacity)
                .ToList();
        }

        private static List<ResourceSearchOutcome> Normalize(IEnumerable<ResourceSearchOutcome> outcomes)
        {
            if (outcomes == null)
                return new List<ResourceSearchOutcome>();

            return outcomes.Where(o => o != null).ToList();
        }

        private static List<ArrivalSimulationResult> Normalize(IEnumerable<ArrivalSimulationResult> simulations)
        {
            if (simulations == null)
                return new List<ArrivalSimulationResult>();

            return simulations.Where(s => s != null).ToList();
        }
    }
}
