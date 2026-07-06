using AirportPlanning.Core.Models;
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
                "Optional: Enter facility index to view history (0=CheckIn, 1=BagDrop, 2=Security, 3=Immigration). If empty, shows latest data.",
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