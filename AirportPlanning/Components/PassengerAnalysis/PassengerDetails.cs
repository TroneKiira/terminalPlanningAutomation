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

            DA.SetDataList(0, passengers.Select(p => p.Id));

            // NEW: Use History if a Facility Index was provided, otherwise use latest state
            DA.SetDataList(1, passengers.Select(p => useHistory && p.History.ContainsKey(facType) ? p.History[facType].ArrivalTime : p.ArrivalTime));
            DA.SetDataList(2, passengers.Select(p => p.ServiceTime)); // Service time is mostly fixed, but you can add it to FacilityRecord if needed
            DA.SetDataList(3, passengers.Select(p => useHistory && p.History.ContainsKey(facType) ? p.History[facType].StartServiceTime : p.StartServiceTime));
            DA.SetDataList(4, passengers.Select(p => useHistory && p.History.ContainsKey(facType) ? p.History[facType].EndServiceTime : p.EndServiceTime));
            DA.SetDataList(5, passengers.Select(p => useHistory && p.History.ContainsKey(facType) ? p.History[facType].WaitTime : p.WaitTime));
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
