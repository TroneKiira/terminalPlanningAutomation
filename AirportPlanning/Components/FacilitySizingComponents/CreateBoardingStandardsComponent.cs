using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using AirportPlanning.Facility_Sizing.Standards;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    public class CreateBoardingStandardsComponent : GH_Component
    {
        public CreateBoardingStandardsComponent()
          : base("Boarding Standards", "B-STD",
            "Defines the architectural and operational standards for Boarding Gates / Hold Rooms.",
            "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Critical Aircraft Capacity", "AC", "Number of seats on the design aircraft.", GH_ParamAccess.item, 180);
            pManager.AddNumberParameter("Passenger Load Factor", "PLF", "Percentage of seats filled (0.0 to 1.0).", GH_ParamAccess.item, 0.85);
            pManager.AddNumberParameter("Turnaround Time", "TAT", "Minutes the aircraft occupies the gate.", GH_ParamAccess.item, 60.0);
            pManager.AddNumberParameter("Gate Utilization", "U", "Efficiency buffer for gate scheduling (0.0 to 1.0).", GH_ParamAccess.item, 0.80);
            pManager.AddNumberParameter("Gate Show-Up Rate", "SUR", "Percentage of pax who wait in the hold room (0.0 to 1.0).", GH_ParamAccess.item, 0.85);
            pManager.AddNumberParameter("Seating Provision", "SEAT%", "Percentage of hold room pax who get a physical seat (0.0 to 1.0).", GH_ParamAccess.item, 0.60);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Standards", "STD", "The Boarding Standards object.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int ac = 180;
            double plf = 0.85, tat = 60.0, u = 0.80, sur = 0.85, seat = 0.60;

            DA.GetData(0, ref ac);
            DA.GetData(1, ref plf);
            DA.GetData(2, ref tat);
            DA.GetData(3, ref u);
            DA.GetData(4, ref sur);
            DA.GetData(5, ref seat);

            // Safety checks for percentages
            if (plf > 1.0) plf /= 100.0;
            if (u > 1.0) u /= 100.0;
            if (sur > 1.0) sur /= 100.0;
            if (seat > 1.0) seat /= 100.0;

            BoardingStandards std = new BoardingStandards
            {
                CriticalAircraftCapacity = ac,
                PassengerLoadFactor = plf,
                TurnaroundTime = tat,
                GateUtilization = u,
                GateShowUpRate = sur,
                SeatingProvision = seat,

                // Standard IATA defaults
                SeatedAreaPerPax = 2.0,
                StandingAreaPerPax = 1.35,
                Width = 1.5, // Podium width
                Depth = 1.0, // Podium depth
                CirculationPercent = 20.0
            };

            DA.SetData(0, std);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("EE555555-FF66-AA11-BB22-CC3333333333"); } }
    }
}