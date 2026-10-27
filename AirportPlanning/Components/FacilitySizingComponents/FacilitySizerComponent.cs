using AirportPlanning.Core.Area;
using AirportPlanning.Core.Models;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Sizers;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    public class FacilitySizerComponent : GH_Component
    {
        public FacilitySizerComponent()
          : base("Facility Sizer", "SIZE",
              "Calculates facility area from solver processor counts, Queue Results and Facility Area Standards.",
              "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Queue Results", "QR",
                "Optional queue metrics and pool names from the facility solver.",
                GH_ParamAccess.item);
            pManager[0].Optional = true;

            pManager.AddIntegerParameter(
                "Physical per Pool", "CPP",
                "Installed processor counts from the facility solver, in the same order as its pool names.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Active per Pool", "CAP",
                "Optional staffed processor counts from the facility solver, in pool order.",
                GH_ParamAccess.list);
            pManager[2].Optional = true;

            pManager.AddIntegerParameter(
                "Metric Basis", "MB",
                "Queue metric to use: 0 = design hour, 1 = worst step, 2 = whole run.",
                GH_ParamAccess.item, 0);

            pManager.AddGenericParameter(
                "Area Standards", "A-STD",
                "Unit areas, queue space, support, fixed rooms and gross-up allowances.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Expansion Factor", "EF",
                "Optional uplift on net area before allowances, as a fraction (0.10 = 10%).",
                GH_ParamAccess.item, 0.0);
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Sizing Result", "SR", "The complete spatial program object.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Area", "TA", "Total required facility area (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Queue Area", "QA", "Required queue area (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Processing Area", "PA", "Required processing area (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Circulation Area", "CA", "Required circulation area (sqm).", GH_ParamAccess.item);
            pManager.AddTextParameter("Topology", "TOP", "No facility-specific topology is applied by generic area sizing.", GH_ParamAccess.item);
            pManager.AddTextParameter("Advice", "ADV", "Planning advice, when available.", GH_ParamAccess.list);
            pManager.AddTextParameter("Equipment Names", "EN", "Names of the sized processor pools.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Equipment Counts", "EC", "Installed and staffed processor counts by pool.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Net Area", "NET", "Area before the allowances (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("MEP Area", "MEP", "Mechanical, electrical and plumbing allowance (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Structure Area", "STR", "Structure allowance (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Gross Area", "GROSS", "Total buildable area (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Support Area", "SUP", "Operational support area (sqm).", GH_ParamAccess.item);
            pManager.AddTextParameter("Pool Areas", "PAREA", "One line per solver pool: units, standard and area.", GH_ParamAccess.list);
            pManager.AddTextParameter("Area Breakdown", "BREAK", "The area build-up, net to gross.", GH_ParamAccess.list);
            pManager.AddTextParameter("Warnings", "WARN", "Anything to check in this result.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object rawQueueResults = null;
            List<int> physicalPerPool = new List<int>();
            List<int> activePerPool = new List<int>();
            int metricBasis = 0;
            FacilityAreaStandardSet areaStandards = null;
            double expansionFactor = 0.0;

            DA.GetData(0, ref rawQueueResults);
            if (!DA.GetDataList(1, physicalPerPool)) return;
            DA.GetDataList(2, activePerPool);
            DA.GetData(3, ref metricBasis);
            if (!DA.GetData(4, ref areaStandards)) return;
            DA.GetData(5, ref expansionFactor);

            if (physicalPerPool.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Connect the facility solver's Physical per Pool output.");
                return;
            }

            if (rawQueueResults is Grasshopper.Kernel.Types.GH_ObjectWrapper wrapper)
                rawQueueResults = wrapper.Value;

            QueueResults queueResults = rawQueueResults as QueueResults;
            List<ProcessorPoolInfo> processorPools = ResolveProcessorPools(queueResults);

            ResolveQueueMetrics(
                queueResults,
                metricBasis,
                out int globalPeakQueue,
                out Dictionary<string, int> peakQueueByChannel,
                out Dictionary<string, int> peakQueueByPool);

            int physicalCount = physicalPerPool.Sum(count => Math.Max(0, count));
            int activeCount = activePerPool.Count > 0
                ? activePerPool.Sum(count => Math.Max(0, count))
                : processorPools.Sum(pool => Math.Max(0, pool.ResourceCount));

            if (activePerPool.Count == 0 && activeCount == 0)
                activeCount = physicalCount;

            FacilityOperationalData operationalData = new FacilityOperationalData
            {
                ProcessorCount = physicalCount,
                ActiveProcessorCount = activeCount,
                BaselineProcessorCount = queueResults != null ? queueResults.BaselineProcessorCount : 0,
                PeakQueue = globalPeakQueue,
                AverageWait = queueResults != null ? queueResults.AverageWait : 0.0,
                MaximumWait = queueResults != null ? queueResults.MaxWait : 0.0,
                Wait95 = queueResults != null ? queueResults.GetEvaluationWait95() : 0.0,
                AnalysisMethod = queueResults != null ? AnalysisMethod.Simulation : AnalysisMethod.Analytical
            };

            PassengerSummaryData passengerSummary = new PassengerSummaryData
            {
                TotalPassengers = ResolvePassengerCount(queueResults, metricBasis)
            };

            FacilitySizingContext context = new FacilitySizingContext
            {
                OperationalData = operationalData,
                PassengerSummary = passengerSummary,
                AreaStandards = areaStandards,
                PoolNames = processorPools.Select(pool => pool.Name).ToList(),
                PhysicalPerPool = physicalPerPool,
                ActivePerPool = activePerPool,
                ProcessorPools = processorPools,
                PeakQueueByChannel = peakQueueByChannel,
                PeakQueueByPool = peakQueueByPool,
                GlobalPeakQueue = globalPeakQueue,
                ExpansionFactor = Math.Max(0.0, expansionFactor)
            };

            FacilitySizingResult result = new GenericFacilitySizer().SizeFacility(context);

            DA.SetData(0, result);
            DA.SetData(1, result.TotalArea);
            DA.SetData(2, result.QueueArea);
            DA.SetData(3, result.ProcessingArea);
            DA.SetData(4, result.CirculationArea);
            DA.SetData(5, result.PreferredTopology ?? string.Empty);

            List<string> adviceStrings = result.PlanningAdvice
                .Select(advice => $"[{advice.Severity}] {advice.Recommendation}: {advice.Reason}")
                .ToList();
            DA.SetDataList(6, adviceStrings);
            DA.SetDataList(7, result.EquipmentCounts.Keys.ToList());
            DA.SetDataList(8, result.EquipmentCounts.Values.ToList());
            DA.SetData(9, result.NetArea);
            DA.SetData(10, result.MepArea);
            DA.SetData(11, result.StructureArea);
            DA.SetData(12, result.GrossArea);
            DA.SetData(13, result.SupportArea);
            DA.SetDataList(14, result.PoolAreas);
            DA.SetDataList(15, result.AreaLines);
            DA.SetDataList(16, result.Warnings);

            foreach (string warning in result.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }

        private static List<ProcessorPoolInfo> ResolveProcessorPools(QueueResults queueResults)
        {
            if (queueResults == null)
                return new List<ProcessorPoolInfo>();

            if (queueResults.ProcessorPools != null && queueResults.ProcessorPools.Count > 0)
                return queueResults.ProcessorPools;

            if (queueResults.PoolResults == null)
                return new List<ProcessorPoolInfo>();

            return queueResults.PoolResults
                .Where(pool => pool != null)
                .Select(pool => new ProcessorPoolInfo
                {
                    Name = pool.Name,
                    ResourceCount = pool.ActiveResources,
                    PhysicalResourceCount = pool.PhysicalResources,
                    IsShared = pool.IsShared
                })
                .ToList();
        }

        private static int ResolvePassengerCount(QueueResults queueResults, int metricBasis)
        {
            if (queueResults == null)
                return 0;

            List<Passenger> passengers = queueResults.ProcessedPassengers != null &&
                                         queueResults.ProcessedPassengers.Count > 0
                ? queueResults.ProcessedPassengers
                : queueResults.Passengers;

            if (passengers == null)
                return 0;

            IEnumerable<Passenger> measured = passengers.Where(passenger => passenger != null && !passenger.IsWarmUp);

            if (metricBasis == 0 && queueResults.DesignStepIndex >= 0)
                measured = measured.Where(passenger => passenger.DemandStepIndex == queueResults.DesignStepIndex);

            return measured.Count();
        }

        /// <summary>
        /// Resolves the queue peaks at the selected metric basis. Exact pool
        /// peaks are available for whole-run sizing; design-hour and worst-step
        /// sizing use the matching channel metrics supplied by QR.
        /// </summary>
        private static void ResolveQueueMetrics(
            QueueResults queueResults,
            int metricBasis,
            out int globalPeak,
            out Dictionary<string, int> byChannel,
            out Dictionary<string, int> byPool)
        {
            globalPeak = 0;
            byChannel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            byPool = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (queueResults == null)
                return;

            globalPeak = queueResults.PeakQueueLength;

            if (queueResults.ByChannel != null)
            {
                foreach (KeyValuePair<string, ClassMetrics> entry in queueResults.ByChannel)
                    byChannel[entry.Key] = entry.Value.PeakQueueLength;
            }

            if (metricBasis == 2 && queueResults.PoolResults != null)
            {
                foreach (QueuePoolResult pool in queueResults.PoolResults)
                {
                    if (pool != null && !string.IsNullOrWhiteSpace(pool.Name))
                        byPool[pool.Name] = pool.PeakQueueLength;
                }
            }

            if (queueResults.IntervalMetrics == null || queueResults.IntervalMetrics.Count == 0)
                return;

            if (metricBasis == 1)
            {
                globalPeak = queueResults.IntervalMetrics.Max(metric => metric.PeakQueueLength);
                byChannel.Clear();

                foreach (IntervalQueueMetrics step in queueResults.IntervalMetrics)
                {
                    if (step.ByChannel == null)
                        continue;

                    foreach (KeyValuePair<string, ClassMetrics> entry in step.ByChannel)
                    {
                        if (!byChannel.TryGetValue(entry.Key, out int current) ||
                            entry.Value.PeakQueueLength > current)
                        {
                            byChannel[entry.Key] = entry.Value.PeakQueueLength;
                        }
                    }
                }
            }
            else if (metricBasis == 0 && queueResults.DesignStepIndex >= 0)
            {
                IntervalQueueMetrics design = queueResults.IntervalMetrics
                    .FirstOrDefault(metric => metric.StepIndex == queueResults.DesignStepIndex);

                if (design == null)
                    return;

                globalPeak = design.PeakQueueLength;
                byChannel.Clear();

                if (design.ByChannel != null)
                {
                    foreach (KeyValuePair<string, ClassMetrics> entry in design.ByChannel)
                        byChannel[entry.Key] = entry.Value.PeakQueueLength;
                }
            }
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("CC333333-DD44-EE55-FF66-007777777777"); }
        }
    }
}
