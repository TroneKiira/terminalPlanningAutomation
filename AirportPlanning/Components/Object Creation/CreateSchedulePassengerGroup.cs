using System;
using System.Collections.Generic;
using System.Linq;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using Grasshopper.Kernel;

namespace AirportPlanning.Components.Object_Creation
{
    /// <summary>
    /// A schedule-mode Passenger Group: behaviour profiles plus an explicit
    /// demand type. It deliberately has no DHP input, because the schedule
    /// supplies the demand. The existing Create Passenger Group remains for
    /// DHP-based Windowed Passenger Generator workflows.
    /// </summary>
    public class CreateSchedulePassengerGroup : GH_Component
    {
        public CreateSchedulePassengerGroup()
          : base("Create Schedule Passenger Group", "CSPG",
            "Builds a departure Passenger Group for schedule-driven generation from " +
            "behaviour profiles and an explicit demand type. No DHP is required.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Profiles", "P",
                "Passenger behaviour profiles for this type. Percentages must sum to 100%.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Type", "T",
                "0 = Domestic Departure, 2 = International Departure. " +
                "Arrival types (1, 3) are rejected: arrival demand uses the Deplaning Profile.",
                GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passenger Group", "PG",
                "Schedule-mode Passenger Group.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<PassengerProfile> profiles = new List<PassengerProfile>();
            int type = 0;

            if (!DA.GetDataList(0, profiles))
                return;

            DA.GetData(1, ref type);

            if (type == (int)DHPType.DomesticArrival ||
                type == (int)DHPType.InternationalArrival)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"Type {type} is an arrival type. Schedule passenger generation is " +
                    "departures-only; use the Deplaning Profile for arrival demand.");
                return;
            }

            DHPType groupType = type == (int)DHPType.InternationalDeparture
                ? DHPType.InternationalDeparture
                : DHPType.DomesticDeparture;

            PassengerGroup group = new PassengerGroup
            {
                Type = groupType,
                Profiles = profiles ?? new List<PassengerProfile>()
            };

            if (group.Profiles.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "No passenger profiles supplied.");
            }
            else
            {
                double total = group.Profiles
                    .Where(p => p != null)
                    .Sum(p => p.Percentage);

                if (Math.Abs(total - 100.0) > 0.001)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        $"Profile percentages must sum to 100% (got {total:0.###}%).");
                }
            }

            DA.SetData(0, group);
        }

        protected override System.Drawing.Bitmap Icon => null;

        public override Guid ComponentGuid =>
            new Guid("F7B2C5D3-9A64-4E18-B27C-1D3E4F5A6B78");
    }
}
