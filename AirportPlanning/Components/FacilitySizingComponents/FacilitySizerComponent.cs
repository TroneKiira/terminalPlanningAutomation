using AirportPlanning.Core.Models;
using AirportPlanning.Facilities.PassengerVerification;
using AirportPlanning.Facility_Sizing.Base;
using AirportPlanning.Facility_Sizing.Models;
using AirportPlanning.Facility_Sizing.Sizers;
using AirportPlanning.Facility_Sizing.Standards;
using AirportPlanning.Facility_Sizing.Templates;

using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    public class FacilitySizerComponent : GH_Component
    {
        public FacilitySizerComponent()
          : base("Facility Sizer", "SIZE", "Calculates the required spatial program (areas) for a facility.", "Airport Planning", "Facility Sizing") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Operational Results", "RES", "Queue Results or Verification Results.", GH_ParamAccess.item);
            pManager[0].Optional = true;
            pManager.AddGenericParameter("Passengers", "PAX", "The passenger population.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Standards", "STD", "Facility Standards.", GH_ParamAccess.item);
            pManager.AddGenericParameter("Template", "TPL", "Facility Template.", GH_ParamAccess.item);

            // NEW: Optional input to force the Sizer to use the Physical Count!
            pManager.AddIntegerParameter("Physical Count Override", "PC", "Optional: Plug in the Physical Lanes/Counters here to size the full building.", GH_ParamAccess.item);
            pManager[4].Optional = true;
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
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            object rawResultsObj = null;
            List<Passenger> passengers = new List<Passenger>();
            FacilityStandards standards = null;
            FacilityTemplate template = null;
            int physicalOverride = -1;

            DA.GetData(0, ref rawResultsObj); // Optional
            if (!DA.GetDataList(1, passengers)) return;
            if (!DA.GetData(2, ref standards)) return;
            if (!DA.GetData(3, ref template)) return;
            DA.GetData(4, ref physicalOverride);

            // 1. Build Passenger Summary
            int total = passengers.Count;
            int domestic = passengers.Count(p => p.PassengerType == PassengerType.Domestic);
            int checkedBags = passengers.Count(p => p.HasCheckedBag);
            int transfers = passengers.Count(p => p.Journey == PassengerJourney.Transfer);
            int males = passengers.Count(p => p.Gender == PassengerGender.Male);

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

            // 2. Build Operational Data (🚨 FIX: Safely handle missing simulation data)
            var opData = new FacilityOperationalData();

            if (rawResultsObj != null)
            {
                object rawResults = rawResultsObj;
                if (rawResultsObj is Grasshopper.Kernel.Types.GH_ObjectWrapper wrapper)
                    rawResults = wrapper.Value;

                if (rawResults is QueueResults qr)
                {
                    opData.ProcessorCount = qr.ResourcesUsed > 0 ? qr.ResourcesUsed : qr.Resources.Count;
                    opData.PeakQueue = qr.PeakQueueLength;
                    opData.AverageWait = qr.AverageWait;
                    opData.MaximumWait = qr.MaxWait;
                    opData.Wait95 = qr.Wait95;
                    opData.AnalysisMethod = AnalysisMethod.Simulation;
                }
                else if (rawResults is PassengerVerificationResults pvr)
                {
                    opData.ProcessorCount = pvr.RequiredGates;
                    opData.Throughput = pvr.GateCapacity;
                    opData.Utilization = pvr.Utilization;
                    opData.AnalysisMethod = AnalysisMethod.Analytical;
                }
            }
            else
            {
                // If no simulation was plugged in, default to Analytical mode
                opData.AnalysisMethod = AnalysisMethod.Analytical;
                opData.ProcessorCount = 1; // Prevent multiply-by-zero errors
            }

            if (physicalOverride > 0)
            {
                opData.ProcessorCount = physicalOverride;
            }

            var context = new FacilitySizingContext { OperationalData = opData, PassengerSummary = paxSummary, Standards = standards, Template = template };

            FacilitySizerBase sizer;
            if (template is CheckInTemplate) sizer = new CheckInSizer();
            else if (template is SecurityTemplate) sizer = new SecuritySizer();
            else if (template is ImmigrationTemplate) sizer = new ImmigrationSizer();
            else if (template is BagDropTemplate) sizer = new BagDropSizer();
            else if (template is PassengerVerificationTemplate) sizer = new PassengerVerificationSizer();
            else if (template is BoardingTemplate) sizer = new BoardingSizer();
            else if (template is RetailTemplate) sizer = new RetailSizer(); 
            else return;

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
        }
        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("CC333333-DD44-EE55-FF66-007777777777"); } }
    }
}