using AirportPlanning.Core.Area;
using AirportPlanning.Core.Models;
using AirportPlanning.Facilities.PassengerVerification;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Sizers;
using AirportPlanning.Facility_Sizing.Standards;

using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    public class FacilitySizerComponent : GH_Component
    {
        public FacilitySizerComponent()
          : base("Facility Sizer", "SIZE", "Calculates the required spatial program (areas) for a facility.", "Airport Planning", "Facility Sizing") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Operational Results", "RES", "Optional: Queue Results.", GH_ParamAccess.item);
            pManager[0].Optional = true;
            pManager.AddGenericParameter("Passengers", "PAX", "The passenger population.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Standards", "STD", "Facility Standards.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Facility Type", "T", "0=CheckIn, 1=BagDrop, 2=Security, 3=Immigration, 4=Verification, 5=Boarding, 6=Retail", GH_ParamAccess.item, 0);

            // 🚨 NEW: The Optimize Toggle and Manual Count!
            pManager.AddBooleanParameter("Optimize Sizing", "O", "True = Auto-calculate. False = Use Manual Count.", GH_ParamAccess.item, true);
            pManager.AddIntegerParameter("Manual Count", "C", "The manual number of Gates/Zones to build.", GH_ParamAccess.item, 10);

            pManager.AddIntegerParameter("Metric Basis", "MB",
                "Which operational metric to size against: 0 = Design Hour (default), 1 = Worst Step, 2 = Whole Run.",
                GH_ParamAccess.item, 0);

            pManager.AddIntegerParameter("Design Hour Index", "DH",
                "Design hour step index used for the passenger summary. -1 = infer from passenger tags.",
                GH_ParamAccess.item, -1);

            pManager.AddGenericParameter("Area Standards", "A-STD",
                "Optional: Facility Area Standards. Supplies the m2 per installed unit for each " +
                "facility key, the queue space per passenger, any fixed rooms, and the " +
                "circulation, MEP and structure allowances.",
                GH_ParamAccess.item);
            pManager[8].Optional = true;

            pManager.AddTextParameter("Pool Names", "PN",
                "Optional: the facility's routed pool names, in output order. Defaults to the " +
                "pool names carried by the Queue Results.",
                GH_ParamAccess.list);
            pManager[9].Optional = true;

            pManager.AddIntegerParameter("Physical per Pool", "CPP",
                "Optional: installed units per pool, in the same order. Defaults to the Queue Results.",
                GH_ParamAccess.list);
            pManager[10].Optional = true;

            pManager.AddIntegerParameter("Active per Pool", "CAP",
                "Optional: staffed units per pool, in the same order. Defaults to the Queue Results.",
                GH_ParamAccess.list);
            pManager[11].Optional = true;

            pManager.AddBooleanParameter("Use Routed Pool Areas", "RPA",
                "On: each routed pool is sized from its own unit standard, so counters, kiosks " +
                "and bag-drop units are built from the units each pool actually has. Off: the " +
                "facility's own sizing rule is used.",
                GH_ParamAccess.item, false);
            pManager[12].Optional = true;

            pManager.AddNumberParameter("Expansion Factor", "EF",
                "Optional: uplift applied to the net area before the allowances, as a fraction " +
                "(0.10 = 10%). 0 leaves the net alone.",
                GH_ParamAccess.item, 0.0);
            pManager[13].Optional = true;

            pManager.AddTextParameter("Facility Key per Pool", "FKEY",
                "Optional: the unit standard each pool is sized with, in the same order. " +
                "Leave empty to match each pool by its own name. Use it when a channel name " +
                "differs from the kind of unit it stands for, e.g. 'Kiosk East' -> 'Kiosk'.",
                GH_ParamAccess.list);
            pManager[14].Optional = true;

            pManager.AddBooleanParameter("No Physical Area", "NPA",
                "Optional: mark a pool as a route with no physical counterpart, e.g. an online " +
                "check-in. It contributes no equipment area. In the same order as the pool names.",
                GH_ParamAccess.list);
            pManager[15].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Sizing Result", "SR", "The complete spatial program object.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Area", "TA", "Total required facility area (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Queue Area", "QA", "Required queue area (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Processing Area", "PA", "Required processing area (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Circulation Area", "CA", "Required circulation area (sqm).", GH_ParamAccess.item);
            pManager.AddTextParameter("Topology", "TOP", "Recommended architectural topology.", GH_ParamAccess.item);
            pManager.AddTextParameter("Advice", "ADV", "Planning advice and warnings.", GH_ParamAccess.list);

            // NEW: Output the raw equipment data
            pManager.AddTextParameter("Equipment Names", "EN", "Names of the required equipment.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Equipment Counts", "EC", "Raw integer counts of the required equipment.", GH_ParamAccess.list);

            pManager.AddNumberParameter("Net Area", "NET", "Area before the allowances (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("MEP Area", "MEP", "Mechanical, electrical and plumbing allowance (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Structure Area", "STR", "Structure allowance (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Gross Area", "GROSS", "Total buildable area (sqm).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Support Area", "SUP", "Support area (sqm).", GH_ParamAccess.item);
            pManager.AddTextParameter("Pool Areas", "PAREA", "One line per routed pool: units, standard and area.", GH_ParamAccess.list);
            pManager.AddTextParameter("Area Breakdown", "BREAK", "The area build-up, net to gross.", GH_ParamAccess.list);
            pManager.AddTextParameter("Warnings", "WARN", "Anything to check in this result.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object rawResultsObj = null;
            List<Passenger> passengers = new List<Passenger>();
            FacilityStandards standards = null;
            bool optimize = true;
            int manualCount = 10;
            int metricBasis = 0;
            int designHourIndex = -1;
            int facilityType = 0;
            FacilityAreaStandardSet areaStandards = null;
            List<string> poolNames = new List<string>();
            List<int> physicalPerPool = new List<int>();
            List<int> activePerPool = new List<int>();
            bool useRoutedPoolAreas = false;
            double expansionFactor = 0.0;
            List<string> facilityKeyPerPool = new List<string>();
            List<bool> noPhysicalAreaPerPool = new List<bool>();

            DA.GetData(0, ref rawResultsObj);
            if (!DA.GetDataList(1, passengers)) return;
            if (!DA.GetData(2, ref standards)) return;
            DA.GetData(3, ref facilityType);
            DA.GetData(4, ref optimize);
            DA.GetData(5, ref manualCount);
            DA.GetData(6, ref metricBasis);
            DA.GetData(7, ref designHourIndex);
            DA.GetData(8, ref areaStandards);
            DA.GetDataList(9, poolNames);
            DA.GetDataList(10, physicalPerPool);
            DA.GetDataList(11, activePerPool);
            DA.GetData(12, ref useRoutedPoolAreas);
            DA.GetData(13, ref expansionFactor);
            DA.GetDataList(14, facilityKeyPerPool);
            DA.GetDataList(15, noPhysicalAreaPerPool);

            // Design-step passenger summary: size against the design hour, not
            // the whole multi-hour population.
            bool windowed = passengers.Any(p => p.DemandStepIndex >= 0);

            int designStep = designHourIndex;

            if (designStep < 0 && windowed)
            {
                Passenger designPassenger = passengers.FirstOrDefault(p => p.IsDesignStep);
                designStep = designPassenger != null ? designPassenger.DemandStepIndex : -1;
            }

            List<Passenger> summaryPax = windowed
                ? passengers.Where(p => p.DemandStepIndex == designStep).ToList()
                : passengers.Where(p => !p.IsWarmUp).ToList();

            int total = summaryPax.Count;
            int domestic = summaryPax.Count(p => p.PassengerType == PassengerType.Domestic);
            int checkedBags = summaryPax.Count(p => p.HasCheckedBag);
            int transfers = summaryPax.Count(p => p.Journey == PassengerJourney.Transfer);
            int males = summaryPax.Count(p => p.Gender == PassengerGender.Male);

            var paxSummary = new PassengerSummaryData
            {
                TotalPassengers = total,
                DomesticPassengers = domestic,
                InternationalPassengers = total - domestic,
                CheckedBagPassengers = checkedBags,
                CabinBagPassengers = total - checkedBags,
                TransferPassengers = transfers,
                MalePassengers = males,
                FemalePassengers = total - males
            };

            var opData = new FacilityOperationalData();

            List<ProcessorPoolInfo> processorPools = new List<ProcessorPoolInfo>();
            Dictionary<string, int> peakQueueByChannel =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int globalPeakQueue = 0;

            if (rawResultsObj != null)
            {
                object rawResults = rawResultsObj;
                if (rawResultsObj is Grasshopper.Kernel.Types.GH_ObjectWrapper wrapper) rawResults = wrapper.Value;

                if (rawResults is QueueResults qr)
                {
                    opData.ProcessorCount = qr.PhysicalProcessorCount > 0 ? qr.PhysicalProcessorCount : qr.Resources.Count;
                    opData.ActiveProcessorCount = qr.Resources.Count;
                    opData.BaselineProcessorCount = qr.BaselineProcessorCount; // From the Simulator!
                    opData.AverageWait = qr.AverageWait;
                    opData.MaximumWait = qr.MaxWait;

                    double wait95 = qr.GetEvaluationWait95();
                    int peakQueue = qr.PeakQueueLength;

                    if (metricBasis == 1 && qr.IntervalMetrics != null && qr.IntervalMetrics.Count > 0)
                    {
                        wait95 = qr.IntervalMetrics.Max(m => m.Wait95);
                        peakQueue = qr.IntervalMetrics.Max(m => m.PeakQueueLength);
                    }
                    else if (metricBasis == 2)
                    {
                        wait95 = qr.Wait95;
                        peakQueue = qr.PeakQueueLength;
                    }

                    opData.PeakQueue = peakQueue;
                    opData.Wait95 = wait95;
                    opData.AnalysisMethod = AnalysisMethod.Simulation;

                    processorPools = qr.ProcessorPools ?? new List<ProcessorPoolInfo>();

                    ResolveQueueMetrics(qr, metricBasis, out globalPeakQueue, out peakQueueByChannel);
                }
                else if (rawResults is PassengerVerificationResults pvr)
                {
                    opData.ProcessorCount = pvr.RequiredGates;
                    opData.ActiveProcessorCount = pvr.RequiredGates;
                    opData.BaselineProcessorCount = pvr.BaselineProcessorCount;
                    opData.Throughput = pvr.GateCapacity;
                    opData.Utilization = pvr.Utilization;
                    opData.AnalysisMethod = AnalysisMethod.Analytical;
                }
            }
            else
            {
                opData.AnalysisMethod = AnalysisMethod.Analytical;
                opData.ProcessorCount = 1;
                opData.ActiveProcessorCount = 1;
            }

            // 🚨 THE FIX: Apply the Toggle Logic!
            if (!optimize)
            {
                opData.IsOverridden = true;
                opData.ProcessorCount = manualCount;
            }

            var context = new FacilitySizingContext
            {
                OperationalData = opData,
                PassengerSummary = paxSummary,
                Standards = standards,
                AreaStandards = areaStandards,
                UseRoutedPoolAreas = useRoutedPoolAreas,
                ProcessorPools = processorPools,
                PeakQueueByChannel = peakQueueByChannel,
                GlobalPeakQueue = globalPeakQueue,
                ExpansionFactor = Math.Max(0.0, expansionFactor)
            };

            // The routed pools come from the definition when it names them, and
            // from the queue results otherwise. Counts the definition leaves
            // out are taken from the results pool by pool, so naming the
            // pools without naming their counts still works.
            if (poolNames.Count > 0)
            {
                context.PoolNames = poolNames;
                context.PhysicalPerPool = physicalPerPool;
                context.ActivePerPool = activePerPool;
            }

            context.FacilityKeyPerPool = facilityKeyPerPool;
            context.NoPhysicalAreaPerPool = noPhysicalAreaPerPool;

            FacilitySizerBase sizer;
            switch (facilityType)
            {
                case 0: sizer = new CheckInSizer(); break;
                case 1: sizer = new BagDropSizer(); break;
                case 2: sizer = new SecuritySizer(); break;
                case 3: sizer = new ImmigrationSizer(); break;
                case 4: sizer = new PassengerVerificationSizer(); break;
                case 5: sizer = new BoardingSizer(); break;
                case 6: sizer = new RetailSizer(); break;
                default: return;
            }

            FacilitySizingResult result = sizer.SizeFacility(context);

            DA.SetData(0, result);
            DA.SetData(1, result.TotalArea);
            DA.SetData(2, result.QueueArea);
            DA.SetData(3, result.ProcessingArea);
            DA.SetData(4, result.CirculationArea);
            DA.SetData(5, result.PreferredTopology);

            List<string> adviceStrings = result.PlanningAdvice.Select(a => $"[{a.Severity}] {a.Recommendation}: {a.Reason}").ToList();
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
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            }
        }

        /// <summary>
        /// The queue lengths the waiting area is sized on.
        ///
        /// A shared pool is sized on the whole facility's peak, because
        /// everybody waits in the same queue. A dedicated pool is sized on its
        /// own channel's peak, so separate channels are not sized on each
        /// other's queues. The metric basis decides which run of the
        /// simulation is read: the design hour, the worst step, or the whole
        /// run.
        /// </summary>
        private static void ResolveQueueMetrics(
            QueueResults qr,
            int metricBasis,
            out int globalPeak,
            out Dictionary<string, int> byChannel)
        {
            globalPeak = qr.PeakQueueLength;

            byChannel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (qr.ByChannel != null)
            {
                foreach (KeyValuePair<string, ClassMetrics> entry in qr.ByChannel)
                    byChannel[entry.Key] = entry.Value.PeakQueueLength;
            }

            if (qr.IntervalMetrics == null || qr.IntervalMetrics.Count == 0)
                return;

            if (metricBasis == 1)
            {
                // Worst step: the busiest step of the run, whichever it is.
                int worst = qr.IntervalMetrics.Max(m => m.PeakQueueLength);

                if (worst > globalPeak)
                    globalPeak = worst;

                Dictionary<string, int> merged =
                    new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (IntervalQueueMetrics step in qr.IntervalMetrics)
                {
                    if (step.ByChannel == null)
                        continue;

                    foreach (KeyValuePair<string, ClassMetrics> entry in step.ByChannel)
                    {
                        int peak = entry.Value.PeakQueueLength;

                        if (!merged.TryGetValue(entry.Key, out int current) || peak > current)
                            merged[entry.Key] = peak;
                    }
                }

                byChannel = merged;
            }
            else if (metricBasis == 0 && qr.DesignStepIndex >= 0)
            {
                IntervalQueueMetrics design = qr.IntervalMetrics
                    .FirstOrDefault(m => m.StepIndex == qr.DesignStepIndex);

                if (design != null)
                {
                    globalPeak = design.PeakQueueLength;

                    byChannel = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    if (design.ByChannel != null)
                    {
                        foreach (KeyValuePair<string, ClassMetrics> entry in design.ByChannel)
                            byChannel[entry.Key] = entry.Value.PeakQueueLength;
                    }
                }
            }
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("CC333333-DD44-EE55-FF66-007777777777"); } }
    }
}
