using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Inputs;

namespace AirportPlanning.Components.Object_Creation
{
    /// <summary>
    /// Builds a per-profile PRIMARY channel share table. Three parallel flat
    /// lists are zipped by row: the passenger-profile key, the channel, and the
    /// share. Shares for a profile should sum to 1.0 (100%). Bag Drop is a
    /// separate service stage; do not add it as a primary share.
    /// </summary>
    public class CreateChannelRouting : GH_Component
    {
        public CreateChannelRouting()
          : base("Channel Routing", "CHR",
            "Builds a per-profile channel share table (profile key, channel, share) for passenger generation.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Profile Keys", "K",
                "Passenger-profile name for each row (must match the profile Name).",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Channels", "CH",
                "Primary channel name for each row (Counter, Kiosk or Web). Bag Drop is a separate facility stage.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Shares", "S",
                "Share for each row. Shares for a profile should sum to 1.0 (100%).",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Default Channel", "DEF",
                "Channel used for a profile with no entry.",
                GH_ParamAccess.item,
                DefaultChannelAssigner.CounterChannel);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Channel Routing", "CHR",
                "Channel routing map for the passenger generators.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> keys = new List<string>();
            List<string> channels = new List<string>();
            List<double> shares = new List<double>();
            string defaultChannel = DefaultChannelAssigner.CounterChannel;

            if (!DA.GetDataList(0, keys)) return;
            if (!DA.GetDataList(1, channels)) return;
            if (!DA.GetDataList(2, shares)) return;
            DA.GetData(3, ref defaultChannel);

            ProfileChannelMap map = new ProfileChannelMap
            {
                DefaultChannel = string.IsNullOrWhiteSpace(defaultChannel)
                    ? DefaultChannelAssigner.CounterChannel
                    : defaultChannel.Trim()
            };

            if (keys.Count != channels.Count || keys.Count != shares.Count)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Profile Keys, Channels and Shares should be the same length.");
            }

            int count = Math.Min(keys.Count, Math.Min(channels.Count, shares.Count));

            for (int i = 0; i < count; i++)
            {
                string key = keys[i] == null ? string.Empty : keys[i].Trim();

                if (key.Length == 0)
                    continue;

                ProfileChannelEntry entry = map.Entries.FirstOrDefault(
                    e => string.Equals(e.ProfileName, key, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    entry = new ProfileChannelEntry { ProfileName = key };
                    map.Entries.Add(entry);
                }

                entry.Shares.Add(new ChannelShare
                {
                    Channel = channels[i] == null ? string.Empty : channels[i].Trim(),
                    Share = shares[i]
                });
            }

            foreach (ProfileChannelEntry entry in map.Entries)
            {
                double total = entry.Shares.Sum(s => s.Share);

                if (Math.Abs(total - 1.0) > 0.001)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        $"Channel shares for '{entry.ProfileName}' sum to {Math.Round(total * 100.0)}% (expected 100%).");
                }
            }

            DA.SetData(0, map);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("F1A2B3C4-D5E6-4789-ABCD-EF0123456789"); }
        }
    }
}
