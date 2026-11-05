using AirportPlanning.Core.Analysis;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Generation;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using System.Linq;

namespace AirportPlanning.Components.PassengerAnalysis
{
    public class PassengerDetails : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PassengerDetails class.
        /// </summary>
        public PassengerDetails()
          : base("Passenger Details", "PDT",
        "Displays the properties of a passenger population.",
        "Airport Planning", "Analysis")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population to inspect.", GH_ParamAccess.list);

            // NEW: Add an optional input to select WHICH facility's data you want to see!
            pManager.AddIntegerParameter("Facility Type", "F",
                "Optional: Enter facility index to view history (0=CheckIn, 1=BagDrop, 2=Security, 3=Emigration, 4=Verification, 5=Departure Lounge, 6=Boarding, 7=Terminal Entry). If empty, shows latest data.",
                GH_ParamAccess.item);
            pManager[1].Optional = true; // Make it optional

            pManager.AddBooleanParameter("Blank When No History", "BWH",
                "When on, passengers WITHOUT this facility's history output NaN instead of the stale previous-facility values, so a journey narrative cannot read a skipped stage as a visit.",
                GH_ParamAccess.item, false);
            pManager[2].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("Passenger ID", "ID", "Passenger identifiers.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Arrival Time", "AT", "Passenger arrival times.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Service Time", "ST", "Passenger service time.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Start Service", "SS", "Passenger service start times.", GH_ParamAccess.list);
            pManager.AddNumberParameter("End Service", "ES", "Passenger service completion times.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Waiting Time", "WT", "Passenger waiting times.", GH_ParamAccess.list);
            pManager.AddTextParameter("Assigned Counter", "R", "Assigned processing counter, lane or booth", GH_ParamAccess.list);
            pManager.AddNumberParameter("Walking Speed", "WS", "Passenger walking speed.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Passenger Type", "PT", "Passenger type.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Journey", "J", "Passenger journey classification.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Online Check-In", "OCI", "Online check-in status.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Checked Bag", "BAG", "Checked baggage status.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Requires Check-In", "CHK", "Indicates whether the passenger requires airport check-in.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Requires Bag Drop", "BD", "Indicates whether the passenger requires bag drop.", GH_ParamAccess.list);
            pManager.AddTextParameter("Transfer Scenario", "TS", "Transfer flow, if the passenger is a classified transfer passenger.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Transfer Security", "TSEC", "Transfer passenger is configured to require security screening.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Transfer Immigration", "TIMM", "Transfer passenger is configured to require immigration.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Transfer Emigration", "TEMI", "Transfer passenger is configured to require emigration.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Baggage Recheck", "BR", "Transfer passenger is configured to require baggage recheck.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Transfer Desk", "TDSK", "Transfer passenger is configured to present at a transfer desk.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Security Separate", "TSECS", "Transfer passenger is routed to a dedicated transfer-security pool.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Immigration Separate", "TIMMS", "Transfer passenger is routed to a dedicated transfer-immigration pool.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Emigration Separate", "TEMIS", "Transfer passenger is routed to a dedicated transfer-emigration pool.", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Transfer Rules Applied", "TRULE", "True when the passenger carries explicit scenario processing rules. False means normal passenger behaviour, not a bypass.", GH_ParamAccess.list);
            pManager.AddTextParameter("Bypassed Services", "TBYP", "Services explicitly bypassed by this passenger's scenario rules.", GH_ParamAccess.list);
            pManager.AddTextParameter("Bypass Reason", "TWHY", "Why each service is bypassed. Empty when no service is explicitly bypassed.", GH_ParamAccess.list);

            pManager.AddTextParameter("Security Entry Route", "SEGR", "Security series mode: the entry e-gate pool the passenger used, for example 'EGate' or 'EGate|DomesticDeparture'. Empty outside series mode.", GH_ParamAccess.list);
            pManager.AddTextParameter("Security Screening Route", "SGSR", "Security series mode: the ATRS screening pool the passenger used, for example 'ATRS|InternationalDeparture'. Empty outside series mode.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Security Entry Wait", "SGEW", "Security series mode: queue wait at the entry e-gate stage, minutes.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Security Screening Wait", "SGSW", "Security series mode: queue wait at the ATRS screening stage, minutes.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Security Entry Start", "SGES", "Security series mode: e-gate service start time from the Security history.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Security Entry End", "SGEE", "Security series mode: e-gate service end time from the Security history.", GH_ParamAccess.list);

            pManager.AddBooleanParameter("Has History", "HIST", "True when the passenger carries the viewed facility's history record. Wire this into the narrative to skip stages the passenger never visited.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Walking Minutes", "WMIN", "Walk minutes the passenger carries from the generator and the terminal-entry stage (visible data, no longer folded into the next stage's arrival).", GH_ParamAccess.list);

            pManager.AddTextParameter("Journey Narration", "NARR", "Process-by-process journey narration built from each passenger's History only. Visited stages show pool, route, resource, wait, service and start/end times; a stage without history reads 'not visited', so a skipped stage can never quote another stage's times.", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> passengers = new List<Passenger>();
            if (!DA.GetDataList(0, passengers)) return;

            int facilityIndex = -1;
            bool useHistory = DA.GetData(1, ref facilityIndex);
            FacilityType facType = (FacilityType)facilityIndex;

            bool blankWhenNoHistory = false;
            DA.GetData(2, ref blankWhenNoHistory);

            DA.SetDataList(0, passengers.Select(p => p.Id));

            // NEW: Use History if a Facility Index was provided, otherwise use latest state
            DA.SetDataList(1, passengers.Select(p =>
                useHistory && p.History.ContainsKey(facType)
                    ? p.History[facType].ArrivalTime
                    : (useHistory && blankWhenNoHistory ? double.NaN : p.ArrivalTime)));
            DA.SetDataList(2, passengers.Select(p => p.ServiceTime)); // Service time is mostly fixed, but you can add it to FacilityRecord if needed
            DA.SetDataList(3, passengers.Select(p =>
                useHistory && p.History.ContainsKey(facType)
                    ? p.History[facType].StartServiceTime
                    : (useHistory && blankWhenNoHistory ? double.NaN : p.StartServiceTime)));
            DA.SetDataList(4, passengers.Select(p =>
                useHistory && p.History.ContainsKey(facType)
                    ? p.History[facType].EndServiceTime
                    : (useHistory && blankWhenNoHistory ? double.NaN : p.EndServiceTime)));
            DA.SetDataList(5, passengers.Select(p =>
                useHistory && p.History.ContainsKey(facType)
                    ? p.History[facType].WaitTime
                    : (useHistory && blankWhenNoHistory ? double.NaN : p.WaitTime)));
            DA.SetDataList(6, passengers.Select(p => useHistory && p.History.ContainsKey(facType) ? p.History[facType].AssignedResource.ToString() : p.AssignedCounter.ToString()));

            DA.SetDataList(7, passengers.Select(p => p.WalkingSpeed));
            DA.SetDataList(8, passengers.Select(p => p.PassengerType));
            DA.SetDataList(9, passengers.Select(p => p.Journey));
            DA.SetDataList(10, passengers.Select(p => p.UsesOnlineCheckIn));
            DA.SetDataList(11, passengers.Select(p => p.HasCheckedBag));
            DA.SetDataList(12, passengers.Select(p => p.RequiresCheckIn));
            DA.SetDataList(13, passengers.Select(p => p.RequiresBagDrop));
            DA.SetDataList(14, passengers.Select(p => p.TransferScenario.HasValue
                ? p.TransferScenario.Value.ToFlowLabel()
                : string.Empty));
            DA.SetDataList(15, passengers.Select(p => p.TransferProcessing != null && p.TransferProcessing.RequiresSecurityScreening));
            DA.SetDataList(16, passengers.Select(p => p.TransferProcessing != null && p.TransferProcessing.RequiresImmigration));
            DA.SetDataList(17, passengers.Select(p => p.TransferProcessing != null && p.TransferProcessing.RequiresEmigration));
            DA.SetDataList(18, passengers.Select(p => p.TransferProcessing != null && p.TransferProcessing.RequiresBaggageRecheck));
            DA.SetDataList(19, passengers.Select(p => p.TransferProcessing != null && p.TransferProcessing.RequiresTransferDesk));
            DA.SetDataList(20, passengers.Select(p => p.TransferProcessing != null && p.TransferProcessing.SecurityRouting == TransferFacilityRouting.Separate));
            DA.SetDataList(21, passengers.Select(p => p.TransferProcessing != null && p.TransferProcessing.ImmigrationRouting == TransferFacilityRouting.Separate));
            DA.SetDataList(22, passengers.Select(p => p.TransferProcessing != null && p.TransferProcessing.EmigrationRouting == TransferFacilityRouting.Separate));
            DA.SetDataList(23, passengers.Select(p => p.TransferProcessing != null));
            DA.SetDataList(24, passengers.Select(p =>
            {
                List<TransferService> bypassed = TransferServiceUsageCalculator.GetBypassedServices(p);
                return string.Join(", ", bypassed.Select(service => service.ToLabel()));
            }));
            DA.SetDataList(25, passengers.Select(p =>
                TransferServiceUsageCalculator.GetBypassedServices(p).Count > 0
                    ? "Scenario rule"
                    : string.Empty));

            // Security series mode: both checkpoint stages, so a narrative can
            // show the e-gate queue and the screening queue separately. The
            // Security history record carries the e-gate stage in its entry
            // fields and the screening stage in its main fields.
            DA.SetDataList(26, passengers.Select(p => p.SecurityEntryRoute ?? string.Empty));
            DA.SetDataList(27, passengers.Select(p => p.SecurityScreeningRoute ?? string.Empty));
            DA.SetDataList(28, passengers.Select(p => p.SecurityEntryWait));
            DA.SetDataList(29, passengers.Select(p => p.SecurityScreeningWait));
            DA.SetDataList(30, passengers.Select(p =>
                p.History.TryGetValue(FacilityType.Security, out FacilityRecord record) && record.HasEntryStage
                    ? record.EntryStartServiceTime
                    : double.NaN));
            DA.SetDataList(31, passengers.Select(p =>
                p.History.TryGetValue(FacilityType.Security, out FacilityRecord record) && record.HasEntryStage
                    ? record.EntryEndServiceTime
                    : double.NaN));

            DA.SetDataList(32, passengers.Select(p =>
                useHistory
                    ? (object)p.History.ContainsKey(facType)
                    : (object)true));
            DA.SetDataList(33, passengers.Select(p =>
            {
                double walk = p.WalkingTime;

                if (!double.IsNaN(p.TerminalEntryWalkMinutes) &&
                    !double.IsInfinity(p.TerminalEntryWalkMinutes))
                {
                    walk = Math.Max(walk, Math.Max(0.0, p.TerminalEntryWalkMinutes));
                }

                return walk;
            }));

            // The tested narration path: every visit comes from the passenger's
            // History, in chronological order, with unvisited stages marked
            // "not visited" instead of showing stale state.
            DA.SetDataList(34, passengers.Select(p => PassengerNarrativeBuilder.ToText(p)));
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("B6A5E9A5-22DF-4B45-8276-0DAE058318C4"); }
        }
    }
}
