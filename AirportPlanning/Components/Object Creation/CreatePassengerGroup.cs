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
            "Merges a Design Hour Profile (DHP) with a list of passenger behaviour profiles to create a Passenger Group.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "DHP", "D",
                "Design Hour Profile for this passenger type.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Profiles", "P",
                "Passenger behaviour profiles for this type. Percentages must sum to 100%.",
                GH_ParamAccess.list);
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

            if (!DA.GetData(0, ref dhp)) return;
            if (!DA.GetDataList(1, profiles)) return;

            PassengerGroup group = new PassengerGroup
            {
                DHP = dhp,
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
