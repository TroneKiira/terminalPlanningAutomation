using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

using AirportPlanning.Facility_Sizing.Standards;
namespace AirportPlanning.Components.FacilitySizingComponents
{
    public class CreateFacilityStandardsComponent : GH_Component
    {
        public CreateFacilityStandardsComponent()
          : base("Facility Standards", "STD",
            "Defines the spatial and architectural standards for a facility.",
            "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Facility Type", "T", "0=CheckIn, 1=BagDrop, 2=Security, 3=Immigration, 4=Verification, 5=Boarding", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Queue Area/Pax", "QA", "Square meters required per waiting passenger.", GH_ParamAccess.item, 1.2);
            pManager.AddNumberParameter("Processor Width", "PW", "Physical width of a single desk/lane (m).", GH_ParamAccess.item, 1.5);
            pManager.AddNumberParameter("Processor Depth", "PD", "Physical depth of a single desk/lane (m).", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("Processor Pitch", "PP", "Center-to-center distance between desks/lanes (m).", GH_ParamAccess.item, 1.8);
            pManager.AddNumberParameter("Circulation %", "C%", "Percentage of total area dedicated to circulation.", GH_ParamAccess.item, 25.0);
            pManager.AddNumberParameter("Support %", "S%", "Percentage of processing area dedicated to support.", GH_ParamAccess.item, 10.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Standards", "STD", "The facility standards object.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int type = 0;
            double qArea = 1.2, pWidth = 1.5, pDepth = 2.0, pPitch = 1.8, circ = 25.0, supp = 10.0;

            if (!DA.GetData(0, ref type)) return;
            DA.GetData(1, ref qArea);
            DA.GetData(2, ref pWidth);
            DA.GetData(3, ref pDepth);
            DA.GetData(4, ref pPitch);
            DA.GetData(5, ref circ);
            DA.GetData(6, ref supp);

            FacilityStandards std;

            // Instantiate the correct derived class based on the type
            switch (type)
            {
                case 0: std = new CheckInStandards(); break;
                case 1: std = new BagDropStandards(); break;
                case 2: std = new SecurityStandards(); break;
                case 3: std = new ImmigrationStandards(); break;
                case 4: std = new PassengerVerificationStandards(); break;
                default: std = new FacilityStandards(); break;
            }

            std.QueueAreaPerPassenger = qArea;
            std.Width = pWidth;
            std.Depth = pDepth;
            std.Pitch = pPitch;
            std.CirculationPercent = circ;
            std.SupportPercent = supp;

            DA.SetData(0, std);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("AA111111-BB22-CC33-DD44-EE5555555555"); } }
    }
}