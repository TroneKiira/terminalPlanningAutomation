using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

using AirportPlanning.Core.Models;

namespace AirportPlanning.Components.PassengerAnalysis
{
    /// <summary>
    /// Summarises the generated passenger population by channel and by
    /// passenger profile — the derived "usage out" percentages that mirror the
    /// analytic verifier. Optionally restricted to one demand step.
    /// </summary>
    public class ChannelSummaryComponent : GH_Component
    {
        public ChannelSummaryComponent()
          : base("Channel Summary", "CHSUM",
            "Counts passengers by channel and by profile (optionally for one demand step).",
            "Airport Planning", "Analysis")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "The passenger population to summarise.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Step Index", "SI",
                "Demand step to summarise; -1 means all steps.",
                GH_ParamAccess.item,
                -1);

            pManager.AddBooleanParameter(
                "Skip Warm-Up", "SKIP",
                "When true, warm-up passengers are excluded.",
                GH_ParamAccess.item,
                true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Channel Names", "CHN", "Channel names.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Channel Counts", "CHC", "Passengers per channel.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Channel Percent", "CH%", "Percentage per channel.", GH_ParamAccess.list);

            pManager.AddTextParameter("Profile Names", "PFN", "Passenger-profile names.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Profile Counts", "PFC", "Passengers per profile.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Profile Percent", "PF%", "Percentage per profile.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> passengers = new List<Passenger>();
            int stepIndex = -1;
            bool skipWarmUp = true;

            if (!DA.GetDataList(0, passengers)) return;
            DA.GetData(1, ref stepIndex);
            DA.GetData(2, ref skipWarmUp);

            IEnumerable<Passenger> filtered = passengers;

            if (skipWarmUp)
                filtered = filtered.Where(p => !p.IsWarmUp);

            if (stepIndex >= 0)
                filtered = filtered.Where(p => p.DemandStepIndex == stepIndex);

            List<Passenger> cohort = filtered.ToList();

            Populate(
                cohort,
                p => string.IsNullOrEmpty(p.Channel) ? "(none)" : p.Channel,
                out List<string> channelNames,
                out List<int> channelCounts,
                out List<double> channelPercent);

            Populate(
                cohort,
                p => string.IsNullOrEmpty(p.ProfileName) ? "(unassigned)" : p.ProfileName,
                out List<string> profileNames,
                out List<int> profileCounts,
                out List<double> profilePercent);

            DA.SetDataList(0, channelNames);
            DA.SetDataList(1, channelCounts);
            DA.SetDataList(2, channelPercent);
            DA.SetDataList(3, profileNames);
            DA.SetDataList(4, profileCounts);
            DA.SetDataList(5, profilePercent);
        }

        private static void Populate(
            List<Passenger> cohort,
            Func<Passenger, string> keySelector,
            out List<string> names,
            out List<int> counts,
            out List<double> percents)
        {
            names = new List<string>();
            counts = new List<int>();
            percents = new List<double>();

            int total = cohort.Count;

            foreach (var group in cohort.GroupBy(keySelector).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                int count = group.Count();

                names.Add(group.Key);
                counts.Add(count);
                percents.Add(total > 0 ? Math.Round(100.0 * count / total, 2) : 0.0);
            }
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("B3C4D5E6-F708-49AB-CDEF-012345678901"); }
        }
    }
}
