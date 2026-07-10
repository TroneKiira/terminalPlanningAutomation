using AirportPlanning.Facility_Sizing.Templates;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    public class CreateFacilityTemplateComponent : GH_Component
    {
        public CreateFacilityTemplateComponent()
          : base("Facility Template", "TPL",
            "Loads the architectural intelligence and topology rules for a facility.",
            "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Facility Type", "T", "0=CheckIn, 1=BagDrop, 2=Security, 3=Immigration, 4=Verification, 5=Boarding, 6=Retail", GH_ParamAccess.item, 0);
            pManager.AddTextParameter("Topology Override", "TOP", "Optional: Manually specify the topology.", GH_ParamAccess.item);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Template", "TPL", "The facility template object.", GH_ParamAccess.item);

            // NEW: Output the allowed topologies so the user knows what to type!
            pManager.AddTextParameter("Available Topologies", "OPTS", "The valid topology options for this facility.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int type = 0;
            string customTopology = null;

            if (!DA.GetData(0, ref type)) return;
            DA.GetData(1, ref customTopology);

            FacilityTemplate tpl;

            switch (type)
            {
                case 0: tpl = new CheckInTemplate(); break;
                case 1: tpl = new BagDropTemplate(); break;
                case 2: tpl = new SecurityTemplate(); break;
                case 3: tpl = new ImmigrationTemplate(); break;
                case 4: tpl = new PassengerVerificationTemplate(); break;
                case 5: tpl = new BoardingTemplate(); break;
                case 6: tpl = new RetailTemplate(); break;
                default: tpl = new CheckInTemplate(); break;
            }

            // VALIDATION LOGIC
            if (!string.IsNullOrEmpty(customTopology))
            {
                // Check if the user's input actually exists in the allowed list (ignoring case sensitivity)
                var match = tpl.AvailableTopologies.FirstOrDefault(t => t.Equals(customTopology, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                {
                    tpl.PreferredTopology = match; // It's valid! Override the default.
                }
                else
                {
                    // It's invalid! Throw a warning in Grasshopper.
                    string options = string.Join(", ", tpl.AvailableTopologies);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"'{customTopology}' is not valid for this facility. Valid options are: {options}");
                }
            }

            DA.SetData(0, tpl);
            DA.SetDataList(1, tpl.AvailableTopologies); // Output the list to the canvas
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("BB222222-CC33-DD44-EE55-FF6666666666"); } }
    }
}