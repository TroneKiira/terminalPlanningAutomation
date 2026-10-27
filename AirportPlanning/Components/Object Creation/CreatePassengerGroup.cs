using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace AirportPlanning.Components.Object_Creation
{
    public class CreatePassengerGroup : GH_Component
    {
        public CreatePassengerGroup()
          : base("Create Passenger Group", "CPG",
            "Builds a Passenger Group from behaviour profiles. A DHP is optional in schedule mode; " +
            "set Type instead when the schedule supplies the demand.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "DHP", "D",
                "Design Hour Profile for this passenger type. Optional in schedule mode; " +
                "when omitted, set Type explicitly instead.",
                GH_ParamAccess.item);
            pManager[0].Optional = true;

            pManager.AddGenericParameter(
                "Profiles", "P",
                "Passenger behaviour profiles for this type. Percentages must sum to 100%.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Type", "T",
                "Optional explicit type for schedule mode: 0 = Domestic Departure, " +
                "1 = Domestic Arrival, 2 = International Departure, 3 = International Arrival. " +
                "-1 takes the type from the DHP.",
                GH_ParamAccess.item, -1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passenger Group", "PG",
                "A passenger group combining demand and behaviour profiles.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DHP dhp = null;
            List<PassengerProfile> profiles = new List<PassengerProfile>();
            int type = -1;

            DA.GetData(0, ref dhp);
            if (!DA.GetDataList(1, profiles)) return;
            DA.GetData(2, ref type);

            if (dhp == null && type < 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Connect either a DHP or set Type explicitly (0-3).");
                return;
            }

            PassengerGroup group = new PassengerGroup
            {
                DHP = dhp,
                Type = type >= 0 ? (DHPType)Math.Max(0, Math.Min(3, type)) : (DHPType?)null,
                Profiles = profiles ?? new List<PassengerProfile>()
            };

            List<string> errors = group.GetValidationErrors();

            foreach (string error in errors)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);

            DA.SetData(0, group);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("FBD3CE21-C94A-4045-9C87-E838458E1313"); }
        }
    }
}
