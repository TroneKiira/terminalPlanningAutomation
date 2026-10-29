using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AirportPlanning.Core.Models;
using AirportPlanning.Facilities.PassengerVerification;
using AirportPlanning.Facility_Sizing.Models;

namespace AirportPlanning.Components.Analysis
{
    public class FacilityReporterComponent : GH_Component
    {
        public FacilityReporterComponent()
          : base("Facility Reporter", "REPORT", "Generates a formatted, color-coded executive summary.", "Airport Planning", "Analysis") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Facility Type", "T", "0=CheckIn, 1=BagDrop, 2=Security, 3=Emigration, 4=Verification, 5=Boarding, 6=Retail, 7=Terminal Entry", GH_ParamAccess.item, 0);
            pManager.AddGenericParameter("Operational Results", "RES", "Optional: Queue Results or Verification Results.", GH_ParamAccess.item);
            pManager[1].Optional = true;
            pManager.AddGenericParameter("Sizing Result", "SR", "The Facility Sizing Result.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Target Wait95", "TW", "The target 95th percentile wait time (minutes).", GH_ParamAccess.item, 10.0);
            pManager.AddIntegerParameter("Baseline Count", "BASE", "Optional: The original optimized processor count.", GH_ParamAccess.item);
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Level of Service", "LOS", "The achieved Level of Service (LoS).", GH_ParamAccess.item);
            pManager.AddTextParameter("Executive Summary", "SUM", "Formatted narrative report of the facility's performance.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Facility Peak Time", "FPT", "Simulation minute at which this facility's own peak queue occurred.", GH_ParamAccess.item);
            pManager.AddTextParameter("Step Report", "STEP", "Per-step performance table (Wait95, peak queue, throughput).", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int facilityType = 0;
            object rawResultsObj = null;
            FacilitySizingResult sizingResult = null;
            double targetWait = 10.0;
            int baselineCount = -1;

            if (!DA.GetData(0, ref facilityType)) return;
            DA.GetData(1, ref rawResultsObj);
            if (!DA.GetData(2, ref sizingResult)) return;
            DA.GetData(3, ref targetWait);
            DA.GetData(4, ref baselineCount);

            double wait95 = 0.0;
            double utilization = 0.0;
            double facilityPeakTime = 0.0;
            string stepReport = "";
            int currentCount = sizingResult.ProcessorCount;
            int baselineCountActual = sizingResult.BaselineProcessorCount;
            if (baselineCountActual <= 0) baselineCountActual = currentCount;

            int demand = sizingResult.PeakDemand;
            int domDemand = 0;
            int intDemand = 0;

            if (rawResultsObj != null)
            {
                object rawResults = rawResultsObj;
                if (rawResultsObj is Grasshopper.Kernel.Types.GH_ObjectWrapper wrapper) rawResults = wrapper.Value;

                if (rawResults is QueueResults qr)
                {
                    wait95 = qr.Wait95;
                    demand = qr.ProcessedPassengers.Count;
                    domDemand = qr.ProcessedPassengers.Count(p => p.PassengerType == PassengerType.Domestic);
                    intDemand = qr.ProcessedPassengers.Count(p => p.PassengerType == PassengerType.International);
                    facilityPeakTime = qr.PeakQueueTime;
                    stepReport = BuildStepReport(qr);

                    if (qr.CounterUtilization != null && qr.CounterUtilization.Count > 0)
                    {
                        double sumUtil = 0;
                        foreach (var u in qr.CounterUtilization) sumUtil += u;
                        utilization = sumUtil / qr.CounterUtilization.Count;
                    }
                }
                else if (rawResults is PassengerVerificationResults pvr)
                {
                    wait95 = 0.0;
                    utilization = pvr.Utilization;
                    demand = pvr.EligiblePassengers;
                    if (pvr.Passengers != null)
                    {
                        domDemand = pvr.Passengers.Count(p => p.PassengerType == PassengerType.Domestic);
                        intDemand = pvr.Passengers.Count(p => p.PassengerType == PassengerType.International);
                    }
                }
            }

            string facilityName = "";
            string unitName = "";
            bool isQueue = facilityType <= 3 || facilityType == 7 || facilityType == 8;
            bool isVerification = facilityType == 4;
            bool isBoarding = facilityType == 5;
            bool isRetail = facilityType == 6;

            switch (facilityType)
            {
                case 0: facilityName = "CHECK-IN"; unitName = "Counters"; break;
                case 1: facilityName = "BAG DROP"; unitName = "SBD Units"; break;
                case 2: facilityName = "SECURITY"; unitName = "X-Ray Lanes"; break;
                case 3: facilityName = "EMIGRATION"; unitName = "Active Positions"; break;
                case 4: facilityName = "VERIFICATION"; unitName = "E-Gates"; break;
                case 5: facilityName = "BOARDING"; unitName = "Gates"; break;
                case 6: facilityName = "RETAIL"; unitName = "Zones"; break;
                case 7: facilityName = "TERMINAL ENTRY"; unitName = "Entry Positions"; break;
                case 8: facilityName = "IMMIGRATION"; unitName = "Active Positions"; break;
                default: facilityName = "FACILITY"; unitName = "Processors"; break;
            }

            // ---------------------------------------------------------
            // COLOR PALETTE
            // ---------------------------------------------------------
            string cLabel = "[119,119,119]"; // Grey for labels
            string cVal = "[220,220,220]";   // Light Grey/White for standard values
            string cGreen = "[76,175,80]";   // Optimum
            string cBlue = "[33,150,243]";   // Over-Design
            string cOrange = "[255,152,0]";  // Sub-Optimum
            string cRed = "[244,67,54]";     // Under-Provided / Critical

            // ---------------------------------------------------------
            // DETERMINE LOS & DYNAMIC COLORS
            // ---------------------------------------------------------
            string losGrade = "Optimum";
            string losColor = cGreen;

            if (isQueue || isVerification)
            {
                if (wait95 > targetWait + 2.0 || utilization > 0.95)
                {
                    losGrade = "Under-Provided"; losColor = cRed;
                }
                else if (wait95 > targetWait || utilization > 0.85)
                {
                    losGrade = "Sub-Optimum"; losColor = cOrange;
                }
                else if (wait95 < (targetWait * 0.3) && utilization < 0.50)
                {
                    losGrade = "Over-Design"; losColor = cBlue;
                }
            }
            else
            {
                losGrade = "Optimum (Analytical)";
            }

            // Override label if it's a manual override
            if (baselineCountActual > 0 && currentCount != baselineCountActual)
            {
                if (currentCount > baselineCountActual) { losGrade = "Over-Design"; losColor = cBlue; }
            }

            // ---------------------------------------------------------
            // 1. HEADER BLOCK
            // ---------------------------------------------------------
            string header = $"{{*{facilityName}*}}{cLabel} : {{*{losGrade.ToUpper()}*}}{losColor}";
            if (baselineCountActual > 0 && currentCount != baselineCountActual)
            {
                header += $" {{*(MANUAL OVERRIDE)*}}{cLabel}";
            }
            header += "\n\n";

            // ---------------------------------------------------------
            // 2. STATS BLOCK
            // ---------------------------------------------------------
            string splitText = "";
            if (demand > 0 && (domDemand > 0 || intDemand > 0))
            {
                if (domDemand == demand) splitText = $" {{*[100% Domestic]*}}{cLabel}";
                else if (intDemand == demand) splitText = $" {{*[100% International]*}}{cLabel}";
                else splitText = $" {{*[Dom: {domDemand} | Int: {intDemand}]*}}{cLabel}";
            }

            double areaPerPax = demand > 0 ? sizingResult.TotalArea / demand : 0.0;

            string statsBlock = $"{{*Demand:*}}{cLabel} {{*{demand} pax/hr*}}{cVal}{splitText}\n" +
                                $"{{*Total Area:*}}{cLabel} {{*{Math.Round(sizingResult.TotalArea, 1)} m²*}}{cVal}\n";

            if (facilityType == 4)
            {
                statsBlock += $"{{*Utilization:*}}{cLabel} {{*{Math.Round(utilization * 100, 1)}%*}}{losColor}\n";
            }
            else if (facilityType < 4 && rawResultsObj != null)
            {
                statsBlock += $"{{*Wait95:*}}{cLabel} {{*{Math.Round(wait95, 1)} mins*}}{losColor} {{*(Target: < {Math.Round(targetWait, 1)} mins)*}}{cLabel}\n" +
                              $"{{*Utilization:*}}{cLabel} {{*{Math.Round(utilization * 100, 1)}%*}}{losColor}\n";

                if (facilityPeakTime > 0.0)
                    statsBlock += $"{{*Facility Peak:*}}{cLabel} {{*min {Math.Round(facilityPeakTime, 0)}*}}{cVal}\n";
            }
            statsBlock += "\n";

            // ---------------------------------------------------------
            // 3. EQUIPMENT BLOCK
            // ---------------------------------------------------------
            string equipmentBlock = $"{{*Equipment Breakdown:*}}{cLabel}\n";
            if (sizingResult.EquipmentCounts != null && sizingResult.EquipmentCounts.Count > 0)
            {
                foreach (var kvp in sizingResult.EquipmentCounts)
                    equipmentBlock += $"  > {{*{kvp.Value}*}}{cVal} {{*{kvp.Key}*}}{cLabel}\n";
            }
            else
            {
                equipmentBlock += $"  > {{*{currentCount}*}}{cVal} {{*{unitName}*}}{cLabel}\n";
            }
            equipmentBlock += "\n";

            // ---------------------------------------------------------
            // 4. ANALYSIS NARRATIVE (With Queuing Theory Logic)
            // ---------------------------------------------------------
            string baseNarrative = "", overNarrative = "", subNarrative = "";
            double increasePct = baselineCountActual > 0 ? Math.Round(((double)(currentCount - baselineCountActual) / baselineCountActual) * 100, 1) : 0;
            double decreasePct = baselineCountActual > 0 ? Math.Round(((double)(baselineCountActual - currentCount) / baselineCountActual) * 100, 1) : 0;
            double excessArea = baselineCountActual > 0 ? Math.Round((sizingResult.TotalArea / currentCount) * (currentCount - baselineCountActual), 1) : 0;

            string analysisPrefix = $"{{*Analysis:*}}{cLabel} ";

            // Dynamic color strings for the narrative text
            string incStr = $"{{*+{increasePct}%*}}{cBlue}";
            string decStr = losGrade == "Under-Provided" ? $"{{*-{decreasePct}%*}}{cRed}" : $"{{*-{decreasePct}%*}}{cOrange}";

            if (isBoarding)
            {
                baseNarrative = $"Operating at IATA {{*{losGrade}*}}{losColor} LoS. The spatial program provides {{*{currentCount}*}}{cVal} Gate Lounges to service the flight schedule. The footprint yields {{*{Math.Round(areaPerPax, 2)} m²/pax*}}{cVal}, successfully balancing seated and standing passenger comfort per ADRM guidelines.";
                overNarrative = $"Capacity increased by {incStr} (Baseline: {{*{baselineCountActual}*}}{cVal} {unitName}). Provides excess gate availability for schedule disruptions, but requires massive unjustified CAPEX/OPEX and an excess footprint of {{*~{excessArea} m²*}}{cBlue}.";
                subNarrative = $"Capacity restricted by {decStr} (Baseline: {{*{baselineCountActual}*}}{cVal} {unitName}). Insufficient gates to handle the flight schedule turnaround times. Will cause severe aircraft holding delays and terminal congestion.";
            }
            else if (isRetail)
            {
                baseNarrative = $"Operating at Commercial {{*{losGrade}*}}{losColor}. The spatial program allocates {{*{Math.Round(sizingResult.TotalArea, 1)} m²*}}{cVal} of commercial space for {{*{demand}*}}{cVal} peak passengers ({{*{Math.Round(areaPerPax, 2)} m²/pax*}}{cVal}). Space is dynamically scaled to capture maximum non-aeronautical revenue based on simulated dwell times.";
                overNarrative = $"Capacity increased by {incStr} (Baseline: {{*{baselineCountActual}*}}{cVal} {unitName}). Over-saturates the terminal with retail space beyond what the dwell time supports, risking low tenant profitability and empty storefronts.";
                subNarrative = $"Capacity restricted by {decStr} (Baseline: {{*{baselineCountActual}*}}{cVal} {unitName}). Fails to capture the commercial yield potential of the passengers' dwell time. Results in lost non-aeronautical revenue and overcrowded F&B zones.";
            }
            else if (isVerification)
            {
                baseNarrative = $"Operating at target utilization. The provision of {{*{currentCount}*}}{cVal} {unitName} successfully processes the demand. Spatial allocation is highly efficient. No CAPEX/OPEX adjustments required.";
                overNarrative = $"Capacity increased by {incStr} (Baseline: {{*{baselineCountActual}*}}{cVal} {unitName}). Utilization has dropped to {{*{Math.Round(utilization * 100, 1)}%*}}{losColor}, indicating over-provisioning. Provides high operational resilience but requires unjustified CAPEX/OPEX and an excess footprint of {{*~{excessArea} m²*}}{cBlue}.";
                subNarrative = $"Capacity restricted by {decStr} (Baseline: {{*{baselineCountActual}*}}{cVal} {unitName}). Elevated utilization ({{*{Math.Round(utilization * 100, 1)}%*}}{losColor}) pushes the system past the queuing theory threshold. Any stochastic variance will cause exponential queue growth with zero recovery buffer.";
            }
            else // QUEUES
            {
                baseNarrative = $"Operating at IATA {{*{losGrade}*}}{losColor} LoS. The calculated capacity successfully absorbs the {{*{demand} pax/hr*}}{cVal} demand without breaching the {Math.Round(targetWait, 1)} mins wait threshold. The spatial footprint yields a provision of {{*{Math.Round(areaPerPax, 2)} m²/pax*}}{cVal}. No CAPEX/OPEX adjustments required.";
                overNarrative = $"Capacity increased by {incStr} (Baseline: {{*{baselineCountActual}*}}{cVal} {unitName}). Wait times are minimal ({{*{Math.Round(wait95, 1)} mins*}}{losColor}), but {{*{Math.Round(utilization * 100, 1)}%*}}{losColor} utilization indicates over-provisioning. Provides high operational resilience but requires unjustified CAPEX/OPEX and an excess footprint of {{*~{excessArea} m²*}}{cBlue}.";

                if (wait95 <= targetWait)
                    subNarrative = $"Capacity restricted by {decStr} (Baseline: {{*{baselineCountActual}*}}{cVal} {unitName}). The facility surprisingly maintains an IATA {{*{losGrade}*}}{losColor} LoS with a Wait95 of {{*{Math.Round(wait95, 1)} mins*}}{losColor}, but utilization is elevated ({{*{Math.Round(utilization * 100, 1)}%*}}{losColor}). The system is highly fragile to stochastic variance with minimal recovery buffer.";
                else
                    subNarrative = $"Capacity restricted by {decStr} (Baseline: {{*{baselineCountActual}*}}{cVal} {unitName}). Violates IATA targets with a Wait95 of {{*{Math.Round(wait95, 1)} mins*}}{losColor}. Utilization is critically high ({{*{Math.Round(utilization * 100, 1)}%*}}{losColor}), pushing the facility into an exponential queue growth state (Kingman's formula threshold). Zero recovery buffer available.";
            }

            string finalSummary = header + statsBlock + equipmentBlock + analysisPrefix;

            if (baselineCountActual <= 0 || currentCount == baselineCountActual)
                finalSummary += baseNarrative;
            else if (currentCount > baselineCountActual)
                finalSummary += overNarrative;
            else if (currentCount < baselineCountActual)
                finalSummary += subNarrative;

            DA.SetData(0, losGrade);
            DA.SetData(1, finalSummary);
            DA.SetData(2, facilityPeakTime);
            DA.SetData(3, stepReport);
        }

        private static string BuildStepReport(QueueResults qr)
        {
            if (qr.IntervalMetrics == null || qr.IntervalMetrics.Count == 0)
                return "";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Step | Arrivals | Wait95 | PeakQ | Thr/hr | PeakMin");

            foreach (IntervalQueueMetrics m in qr.IntervalMetrics)
            {
                sb.AppendLine(
                    $"{m.StepIndex} | {m.ArrivalCount} | {Math.Round(m.Wait95, 1)} | " +
                    $"{m.PeakQueueLength} | {Math.Round(m.Throughput, 0)} | {Math.Round(m.PeakQueueTime, 0)}");
            }

            return sb.ToString();
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("99887766-5544-3322-1100-AABBCCDDEEFF"); } }
    }
}