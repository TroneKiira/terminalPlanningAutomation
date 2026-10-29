using AirportPlanning.Core.Gates;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    /// <summary>
    /// PHP stand policy and shared area-per-passenger standards. Per-code
    /// passenger assumptions come only from Gate Lounge Profiles.
    /// </summary>
    public class CreateGateStandardsComponent : GH_Component
    {
        public CreateGateStandardsComponent()
            : base("Gate Standards", "G-STD",
                "PHP stand policy and shared gate area standards. Per-code passenger profiles are supplied separately.",
                "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter(
                "Buffer Minutes", "BUF",
                "PHP occupancy buffer added to each aircraft turn. Replaced by schedule-specific before/after buffers in schedule mode.",
                GH_ParamAccess.item, 10.0);

            pManager.AddNumberParameter(
                "Spares %", "SP%",
                "Spare stands as a share of the peak requirement; accepts fractions or percentage points (0.05 or 5).",
                GH_ParamAccess.item, 0.0);

            pManager.AddTextParameter(
                "Swing Codes", "SWC",
                "PHP aircraft codes with manually stated swing stands. Replaced by schedule-derived swing in schedule mode.",
                GH_ParamAccess.list);
            pManager[2].Optional = true;

            pManager.AddIntegerParameter(
                "Swing Stands", "SW",
                "PHP swing-stand count for each listed code. Replaced by schedule-derived swing in schedule mode.",
                GH_ParamAccess.list);
            pManager[3].Optional = true;

            pManager.AddNumberParameter(
                "Contact % Domestic", "CD%",
                "Share of domestic active stands with a bridge; accepts fractions or percentage points (0.5 or 50).",
                GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter(
                "Contact % Swing", "CS%",
                "Share of swing active stands with a bridge; accepts fractions or percentage points (0.7 or 70).",
                GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter(
                "Contact % International", "CI%",
                "Share of international active stands with a bridge; accepts fractions or percentage points (0.8 or 80).",
                GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter(
                "Remote Stands per Bus Gate", "RPB",
                "Remote stands served by one bus gate. Used in either demand mode.",
                GH_ParamAccess.item, 1.0);

            pManager.AddNumberParameter(
                "Max ÷ Active Stands", "MAX",
                "PHP total-to-active stand multiplier. Ignored in schedule mode, where max stands equal the measured active peak.",
                GH_ParamAccess.item, 1.0);

            pManager.AddNumberParameter(
                "Seated Space m2", "SS",
                "Shared area per seated passenger, m2.",
                GH_ParamAccess.item, 1.7);

            pManager.AddNumberParameter(
                "Standing Space m2", "TS",
                "Shared area per standing passenger, m2.",
                GH_ParamAccess.item, 1.2);

            pManager.AddNumberParameter(
                "Boarding Queue Space m2", "QS",
                "Shared area per passenger waiting in the boarding queue, m2.",
                GH_ParamAccess.item, 1.2);

            pManager.AddNumberParameter(
                "Podium Area each m2", "PODA",
                "Shared floor area for one podium position, m2. Podium count is per code in Gate Lounge Profiles.",
                GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Gate Standards", "STD",
                "Stand policy and shared passenger-space standards.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double buffer = 10.0;
            double spares = 0.0;
            List<string> swingCodes = new List<string>();
            List<int> swingStands = new List<int>();
            double contactDom = 0.0;
            double contactSwing = 0.0;
            double contactIntl = 0.0;
            double remotePerBus = 1.0;
            double maxRatio = 1.0;
            double seatedSpace = 1.7;
            double standingSpace = 1.2;
            double queueSpace = 1.2;
            double podiumArea = 0.0;

            DA.GetData(0, ref buffer);
            DA.GetData(1, ref spares);
            DA.GetDataList(2, swingCodes);
            DA.GetDataList(3, swingStands);
            DA.GetData(4, ref contactDom);
            DA.GetData(5, ref contactSwing);
            DA.GetData(6, ref contactIntl);
            DA.GetData(7, ref remotePerBus);
            DA.GetData(8, ref maxRatio);
            DA.GetData(9, ref seatedSpace);
            DA.GetData(10, ref standingSpace);
            DA.GetData(11, ref queueSpace);
            DA.GetData(12, ref podiumArea);

            GateStandards standards = new GateStandards
            {
                BufferMinutes = Math.Max(0.0, buffer),
                SparesPct = Share(spares),
                ContactPctDomestic = Share(contactDom),
                ContactPctSwing = Share(contactSwing),
                ContactPctInternational = Share(contactIntl),
                RemoteStandsPerBusGate = Math.Max(0.0, remotePerBus),
                MaxStandsRatio = Math.Max(0.0, maxRatio),
                SeatedSpacePerPax = Math.Max(0.0, seatedSpace),
                StandingSpacePerPax = Math.Max(0.0, standingSpace),
                BoardingQueueSpacePerPax = Math.Max(0.0, queueSpace),
                PodiumAreaEach = Math.Max(0.0, podiumArea)
            };

            int swingCount = Math.Min(swingCodes.Count, swingStands.Count);
            for (int i = 0; i < swingCount; i++)
            {
                string code = swingCodes[i];
                if (!string.IsNullOrWhiteSpace(code))
                    standards.SwingStands[code.Trim()] = Math.Max(0, swingStands[i]);
            }

            if (swingCodes.Count != swingStands.Count)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Swing Codes and Swing Stands are read in pairs; " +
                    $"{swingCodes.Count} code(s) and {swingStands.Count} value(s) were given.");
            }

            DA.SetData(0, standards);
        }

        /// <summary>Accepts fractional shares or percentage points.</summary>
        private static double Share(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0.0;

            double share = value > 1.0 ? value / 100.0 : value;
            return Math.Min(1.0, Math.Max(0.0, share));
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("6C2E9A17-4B58-4D31-9F0A-7E15C8B3D642"); }
        }
    }
}
