using System;
using System.Collections.Generic;

using AirportPlanning.Core.Queueing;
using AirportPlanning.Components;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace AirportPlanning.Components.Object_Creation
{
    /// <summary>
    /// Builds the keyed pool plans that facility solvers consume. Rows are
    /// zipped by index: pool identity, nominal resources, target wait and
    /// resource domain. Identity is "Channel" or "Channel|PoolKey"; use
    /// "Shared" (or leave blank) for the legacy single-pool case.
    /// </summary>
    public class CreatePoolPlans : GH_Component
    {
        public CreatePoolPlans()
          : base("Pool Plans", "POOLPLAN",
            "Builds per-pool sizing plans (nominal resources, target wait, resource domain) " +
            "for the facility solvers.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Pool Identities", "ID",
                "One row per pool: 'Channel' or 'Channel|PoolKey' (e.g. " +
                "'Counter|DomesticDeparture|Economy'). Use 'Shared' for one facility-wide pool. " +
                "Bag Drop pools must be keyed 'BagDrop' or 'BagDrop|DHPType|ProfileName' — the " +
                "key selects a segment of the bag-drop population and is never matched against " +
                "a passenger's primary check-in channel.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Nominal Resources", "PNR",
                "Nominal physical resource count per row (PNR). Used as the fixed " +
                "count when optimization is off and reported as the nominal benchmark.",
                GH_ParamAccess.list, 1.0);

            pManager.AddNumberParameter(
                "Target Wait", "PTW",
                "Target whole-run Wait95 in minutes per row (MQT).",
                GH_ParamAccess.list, 10.0);

            pManager.AddIntervalParameter(
                "Resource Domains", "PDOMAIN",
                "Inclusive Min/Max resource bounds per row. Leave unwired to inherit " +
                "each solver's fallback Resource Domain.",
                GH_ParamAccess.list);

            pManager[3].Optional = true;

            pManager.AddTextParameter(
                "Facilities", "FAC",
                "Optional facility tag per row, e.g. 'Security' or 'Emigration'. " +
                "A tagged row is only used by that facility, so the same identity " +
                "(for example 'EGate') can carry different values at two facilities. " +
                "Leave blank to apply to any facility.",
                GH_ParamAccess.list);

            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Pool Plans", "PP",
                "Keyed pool plans for Check-In, Bag Drop, Security or Emigration.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> identities = new List<string>();
            List<double> nominal = new List<double>();
            List<double> targets = new List<double>();
            List<Interval> domains = new List<Interval>();
            List<string> facilities = new List<string>();

            if (!DA.GetDataList(0, identities)) return;
            DA.GetDataList(1, nominal);
            DA.GetDataList(2, targets);
            DA.GetDataList(3, domains);
            DA.GetDataList(4, facilities);

            if (identities.Count == 0)
                return;

            WarnIfMismatched(nominal, identities.Count, "Nominal Resources");
            WarnIfMismatched(targets, identities.Count, "Target Wait");
            WarnIfMismatched(domains, identities.Count, "Resource Domains");
            WarnIfMismatched(facilities, identities.Count, "Facilities");

            List<PoolPlan> plans = new List<PoolPlan>(identities.Count);

            for (int i = 0; i < identities.Count; i++)
            {
                string identity = identities[i] == null ? string.Empty : identities[i].Trim();

                if (identity.Length == 0)
                    continue;

                PoolPlan plan = new PoolPlan
                {
                    Facility = PickString(facilities, i),
                    PhysicalResources = PickInt(nominal, i),
                    TargetWait95 = PickDouble(targets, i),
                    Range = PickDomain(domains, i)
                };

                if (string.Equals(identity, "Shared", StringComparison.OrdinalIgnoreCase))
                {
                    plan.Channel = null;
                    plan.PoolKey = null;
                }
                else
                {
                    int separator = identity.IndexOf('|');

                    plan.Channel = separator < 0
                        ? identity
                        : identity.Substring(0, separator).Trim();

                    plan.PoolKey = separator < 0
                        ? null
                        : identity.Substring(separator + 1).Trim();

                    if (string.IsNullOrWhiteSpace(plan.Channel))
                    {
                        AddRuntimeMessage(
                            GH_RuntimeMessageLevel.Warning,
                            $"Row {i + 1}: the pool identity '{identity}' has no channel and was skipped.");
                        continue;
                    }
                }

                plans.Add(plan);
            }

            DA.SetDataList(0, plans);
        }

        private void WarnIfMismatched<T>(IList<T> values, int rowCount, string label)
        {
            if (values == null || values.Count == 0 || values.Count == 1 || values.Count == rowCount)
                return;

            AddRuntimeMessage(
                GH_RuntimeMessageLevel.Warning,
                $"{label} has {values.Count} values but {rowCount} pools are listed; " +
                "a single value broadcasts, otherwise values are paired in order.");
        }

        private static int PickInt(IList<double> values, int index)
        {
            if (values == null || values.Count == 0)
                return 1;

            int slot = values.Count == 1 ? 0 : Math.Min(index, values.Count - 1);
            return Math.Max(1, (int)Math.Round(values[slot]));
        }

        private static string PickString(IList<string> values, int index)
        {
            if (values == null || values.Count == 0)
                return null;

            int slot = values.Count == 1 ? 0 : Math.Min(index, values.Count - 1);

            string value = values[slot];

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static double PickDouble(IList<double> values, int index)
        {
            if (values == null || values.Count == 0)
                return 10.0;

            int slot = values.Count == 1 ? 0 : Math.Min(index, values.Count - 1);
            return values[slot] <= 0.0 ? 0.0 : values[slot];
        }

        private ResourceSearchRange PickDomain(IList<Interval> values, int index)
        {
            if (values == null || values.Count == 0)
                return null;

            int slot = values.Count == 1 ? 0 : Math.Min(index, values.Count - 1);

            return FacilityComponentInput.ToResourceDomain(values[slot]);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("B5C6D7E8-F9A0-4B1C-8D2E-3F4051627384"); }
        }
    }
}
