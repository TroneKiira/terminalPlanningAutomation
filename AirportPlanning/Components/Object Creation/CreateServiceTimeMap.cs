using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

using AirportPlanning.Core.Inputs;

namespace AirportPlanning.Components.Object_Creation
{
    /// <summary>
    /// Builds a service-time map. Two parallel flat lists are zipped by row:
    /// a key and its service profile. Keys may use the Pool Plans identity
    /// "channel|DHPType|profile", or a profile name, channel name,
    /// "profile|channel", "DHPType|profile", or "DHPType|profile|channel".
    /// </summary>
    public class CreateServiceTimeMap : GH_Component
    {
        public CreateServiceTimeMap()
          : base("Service Time Map", "STMAP",
            "Maps passengers to service-time profiles by profile/channel and optional DHP type.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Default Profile", "DEF",
                "Service profile used when no key matches.",
                GH_ParamAccess.item);
            pManager[0].Optional = true;

            pManager.AddTextParameter(
                "Keys", "K",
                "Key for each row: 'Channel|DHPType|ProfileName' (same as a Pool Plan identity, e.g. 'Counter|DomesticDeparture|Economy'), or profile, channel, 'profile|channel', 'DHPType|profile', or 'DHPType|profile|channel'.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Service Profiles", "SP",
                "Service profile for each key (same length as Keys).",
                GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Service Time Map", "STMAP",
                "Service-time map for the facility components.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            ServiceProfile defaultProfile = null;
            List<string> keys = new List<string>();
            List<ServiceProfile> profiles = new List<ServiceProfile>();

            DA.GetData(0, ref defaultProfile);

            if (!DA.GetDataList(1, keys)) return;
            if (!DA.GetDataList(2, profiles)) return;

            ServiceTimeMap map = new ServiceTimeMap { Default = defaultProfile };

            if (keys.Count != profiles.Count)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Keys and Service Profiles should be the same length.");
            }

            int count = Math.Min(keys.Count, profiles.Count);

            for (int i = 0; i < count; i++)
            {
                string key = keys[i] == null ? string.Empty : keys[i].Trim();

                if (key.Length == 0 || profiles[i] == null)
                    continue;

                map.Entries.Add(new ServiceTimeEntry
                {
                    Key = key,
                    Profile = profiles[i]
                });
            }

            DA.SetData(0, map);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("A2B3C4D5-E6F7-489A-BCDE-F01234567890"); }
        }
    }
}
