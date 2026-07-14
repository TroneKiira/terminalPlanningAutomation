using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Models;
using AirportPlanning.Facilities.PassengerVerification;
using AirportPlanning.Facility_Sizing.Models;

namespace AirportPlanning.Components.Analysis
{
    public class FacilityReporterComponent : GH_Component
    {
        public FacilityReporterComponent()
          : base("Facility Reporter", "REPORT",
            "Generates a crisp, professional executive summary and LoS grade for a facility.",
            "Airport Planning", "Analysis")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Facility Type", "T", "0=CheckIn, 1=BagDrop, 2=Security, 3=Immigration, 4=Verification, 5=Boarding, 6=Retail", GH_ParamAccess.item, 0);

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
            pManager.AddTextParameter("Executive Summary", "SUM", "Crisp narrative report of the facility's performance.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int facilityType = 0;
            object rawResultsObj = null;
            FacilitySizingResult sizingResult = null;
            double targetWait = 10.0;
            int baselineCount = -1;

            if (!DA.GetData(0, ref facilityType)) return;
            DA.GetData(1, ref rawResultsObj); // Optional
            if (!DA.GetData(2, ref sizingResult)) return;
            DA.GetData(3, ref targetWait);
            DA.GetData(4, ref baselineCount); // Optional

            double wait95 = 0.0;
            double utilization = 0.0;

            // Strictly use the Sizer's physical count (which respects user overrides)
            int currentCount = sizingResult.ProcessorCount;

            // Pull demand from the Sizer so Retail/Boarding aren't 0
            int demand = sizingResult.PeakDemand;
            int domDemand = 0;
            int intDemand = 0;

            // Safely unwrap the Operational Results if they exist
            if (rawResultsObj != null)
            {
                object rawResults = rawResultsObj;
                if (rawResultsObj is Grasshopper.Kernel.Types.GH_ObjectWrapper wrapper)
                    rawResults = wrapper.Value;

                if (rawResults is QueueResults qr)
                {
                    wait95 = qr.Wait95;
                    demand = qr.ProcessedPassengers.Count;
                    domDemand = qr.ProcessedPassengers.Count(p => p.PassengerType == PassengerType.Domestic);
                    intDemand = qr.ProcessedPassengers.Count(p => p.PassengerType == PassengerType.International);

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

            // Facility Terminology
            string facilityName = "";
            string unitName = "";

            switch (facilityType)
            {
                case 0: facilityName = "CHECK-IN"; unitName = "Counters"; break;
                case 1: facilityName = "BAG DROP"; unitName = "SBD Units"; break;
                case 2: facilityName = "SECURITY"; unitName = "X-Ray Lanes"; break;
                case 3: facilityName = "IMMIGRATION"; unitName = "Active Agents"; break;
                case 4: facilityName = "VERIFICATION"; unitName = "E-Gates"; break;
                case 5: facilityName = "BOARDING"; unitName = "Gates"; break;
                case 6: facilityName = "RETAIL"; unitName = "Zones"; break;
                default: facilityName = "FACILITY"; unitName = "Processors"; break;
            }

            // Determine Level of Service (LoS) Grade
            string losGrade = "Optimum";
            if (facilityType < 4) // Only grade Queues on Wait95
            {
                if (wait95 > targetWait + 2.0 || utilization > 0.95) losGrade = "Under-Provided";
                else if (wait95 > targetWait || utilization > 0.85) losGrade = "Sub-Optimum";
                else if (wait95 < (targetWait * 0.3) && utilization < 0.50) losGrade = "Over-Design";
            }
            else
            {
                losGrade = "Optimum (Analytical)";
            }

            // ---------------------------------------------------------
            // FORMAT THE EQUIPMENT BREAKDOWN SEPARATELY
            // ---------------------------------------------------------
            string equipmentBlock = "Equipment Breakdown:\n";

            if (sizingResult.EquipmentCounts != null && sizingResult.EquipmentCounts.Count > 0)
            {
                foreach (var kvp in sizingResult.EquipmentCounts)
                {
                    equipmentBlock += $"  > {kvp.Value} {kvp.Key}\n";
                }
            }
            else
            {
                equipmentBlock += $"  > {currentCount} {unitName}\n";
            }

            // ---------------------------------------------------------
            // FORMAT THE STATS BLOCK (Context-Aware)
            // ---------------------------------------------------------
            string targetWaitStr = Math.Round(targetWait, 1).ToString();

            string splitText = "";
            if (demand > 0 && (domDemand > 0 || intDemand > 0))
            {
                if (domDemand == demand) splitText = " [100% Domestic]";
                else if (intDemand == demand) splitText = " [100% International]";
                else splitText = $" [Dom: {domDemand} | Int: {intDemand}]";
            }

            double areaPerPax = demand > 0 ? sizingResult.TotalArea / demand : 0.0;

            string statsBlock = $"Demand: {demand} pax/hr{splitText} | Total Area: {Math.Round(sizingResult.TotalArea, 1)} m²\n";

            if (facilityType == 4) // Verification
            {
                statsBlock += $"Design Util: {Math.Round(utilization * 100, 1)}%\n";
            }
            else if (facilityType < 4 && rawResultsObj != null) // Queues
            {
                statsBlock += $"Wait95: {Math.Round(wait95, 1)} mins (Target: <{targetWaitStr}m) | Util: {Math.Round(utilization * 100, 1)}%\n";
            }

            statsBlock += $"--------------------------------------------------\n" +
                          equipmentBlock +
                          $"--------------------------------------------------\n";

            // ---------------------------------------------------------
            // SPECIFIC, NUMBER-DRIVEN ANALYSIS NARRATIVES
            // ---------------------------------------------------------
            string baseNarrative = "", overNarrative = "", subNarrative = "";
            double increasePct = baselineCount > 0 ? Math.Round(((double)(currentCount - baselineCount) / baselineCount) * 100, 1) : 0;
            double decreasePct = baselineCount > 0 ? Math.Round(((double)(baselineCount - currentCount) / baselineCount) * 100, 1) : 0;
            double excessArea = baselineCount > 0 ? Math.Round((sizingResult.TotalArea / currentCount) * (currentCount - baselineCount), 1) : 0;

            if (facilityType == 4) // VERIFICATION
            {
                baseNarrative = $"Operating at IATA Optimum LoS. The provision of {currentCount} {unitName} satisfies the {demand} pax/hr demand using deterministic throughput math. Target utilization is maintained. No CAPEX/OPEX adjustments required.";
                overNarrative = $"Capacity increased by +{increasePct}% (Baseline: {baselineCount} {unitName}). Lowers utilization below optimal design targets, indicating over-provisioning. Provides high operational resilience but requires unjustified CAPEX/OPEX and an excess footprint of ~{excessArea} m².";
                subNarrative = $"Capacity restricted by -{decreasePct}% (Baseline: {baselineCount} {unitName}). Pushes utilization beyond safe design limits. High risk of bottlenecking and passenger congestion at the terminal entry.";
            }
            else if (facilityType == 5) // BOARDING
            {
                baseNarrative = $"Operating at IATA Optimum LoS. The spatial program provides {currentCount} Gate Lounges to service the flight schedule. The footprint yields {Math.Round(areaPerPax, 2)} m²/pax, successfully balancing seated and standing passenger comfort per ADRM guidelines.";
                overNarrative = $"Capacity increased by +{increasePct}% (Baseline: {baselineCount} {unitName}). Provides excess gate availability for schedule disruptions, but requires massive unjustified CAPEX/OPEX and an excess footprint of ~{excessArea} m².";
                subNarrative = $"Capacity restricted by -{decreasePct}% (Baseline: {baselineCount} {unitName}). Insufficient gates to handle the flight schedule turnaround times. Will cause severe aircraft holding delays and terminal congestion.";
            }
            else if (facilityType == 6) // RETAIL
            {
                baseNarrative = $"Operating at Commercial Optimum. The spatial program allocates {Math.Round(sizingResult.TotalArea, 1)} m² of commercial space for {demand} peak passengers ({Math.Round(areaPerPax, 2)} m²/pax). Space is dynamically scaled to capture maximum non-aeronautical revenue based on simulated dwell times.";
                overNarrative = $"Capacity increased by +{increasePct}% (Baseline: {baselineCount} {unitName}). Over-saturates the terminal with retail space beyond what the dwell time supports, risking low tenant profitability and empty storefronts.";
                subNarrative = $"Capacity restricted by -{decreasePct}% (Baseline: {baselineCount} {unitName}). Fails to capture the commercial yield potential of the passengers' dwell time. Results in lost non-aeronautical revenue and overcrowded F&B zones.";
            }
            else // QUEUES (Check-In, Security, Immigration, Bag Drop)
            {
                baseNarrative = $"Operating at IATA {losGrade} LoS. The calculated capacity successfully absorbs the {demand} pax/hr demand without breaching the {targetWaitStr}m wait threshold. The spatial footprint yields a provision of {Math.Round(areaPerPax, 2)} m²/pax. No CAPEX/OPEX adjustments required.";
                overNarrative = $"Capacity increased by +{increasePct}% (Baseline: {baselineCount} {unitName}). Wait times are minimal ({Math.Round(wait95, 1)}m), but {Math.Round(utilization * 100, 1)}% utilization indicates over-provisioning. Provides high operational resilience but requires unjustified CAPEX/OPEX and an excess footprint of ~{excessArea} m².";
                subNarrative = $"Capacity restricted by -{decreasePct}% (Baseline: {baselineCount} {unitName}). Violates IATA targets with a Wait95 of {Math.Round(wait95, 1)}m. Critically high utilization ({Math.Round(utilization * 100, 1)}%) leaves zero recovery buffer. High risk of queue spillback into circulation corridors.";
            }

            string summary = "";
            if (baselineCount <= 0 || currentCount == baselineCount)
                summary = $"[ {facilityName} : OPTIMIZED BASELINE ]\n" + statsBlock + "Analysis: " + baseNarrative;
            else if (currentCount > baselineCount)
                summary = $"[ {facilityName} : OVER-DESIGNED (MANUAL OVERRIDE) ]\n" + statsBlock + "Analysis: " + overNarrative;
            else if (currentCount < baselineCount)
                summary = $"[ {facilityName} : SUB-OPTIMUM (MANUAL OVERRIDE) ]\n" + statsBlock + "Analysis: " + subNarrative;

            DA.SetData(0, losGrade);
            DA.SetData(1, summary);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("99887766-5544-3322-1100-AABBCCDDEEFF"); } }
    }
}