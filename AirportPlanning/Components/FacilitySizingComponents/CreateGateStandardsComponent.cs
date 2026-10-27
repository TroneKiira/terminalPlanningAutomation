using AirportPlanning.Core.Gates;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    /// <summary>
    /// The policy and the space standards the gate area is built from: how
    /// long a stand may be held, how many swing and spare stands there are,
    /// how much of the fleet is contact, and how much room a seated passenger,
    /// a standing passenger and a boarding queue need.
    /// </summary>
    public class CreateGateStandardsComponent : GH_Component
    {
        public CreateGateStandardsComponent()
          : base("Gate Standards", "G-STD",
            "Standards for aircraft stands and terminal gate lounges: stand occupancy buffer, spares, swing, contact share, and the space each passenger kind needs.",
            "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter(
                "Buffer Minutes", "BUF",
                "Minutes added to every aircraft's occupancy: a stand cannot turn from one aircraft to the next at no cost.",
                GH_ParamAccess.item, 10.0);

            pManager.AddNumberParameter(
                "Spares %", "SP%",
                "Spare stands, as a percentage of the peak requirement.",
                GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter(
                "Load Factor", "LF",
                "Share of the seats filled, 0 to 1.",
                GH_ParamAccess.item, 0.85);

            pManager.AddTextParameter(
                "Swing Codes", "SWC",
                "Aircraft codes that have swing stands, e.g. C and E.",
                GH_ParamAccess.list);
            pManager[3].Optional = true;

            pManager.AddIntegerParameter(
                "Swing Stands", "SW",
                "Stands of each listed code able to serve either sector.",
                GH_ParamAccess.list);
            pManager[4].Optional = true;

            pManager.AddNumberParameter(
                "Contact % Domestic", "CD%",
                "Share of the domestic group's active stands with a bridge attached, 0 to 1.",
                GH_ParamAccess.item, 0.8);

            pManager.AddNumberParameter(
                "Contact % Swing", "CS%",
                "Share of the swing group's active stands with a bridge attached, 0 to 1.",
                GH_ParamAccess.item, 0.8);

            pManager.AddNumberParameter(
                "Contact % International", "CI%",
                "Share of the international group's active stands with a bridge attached, 0 to 1.",
                GH_ParamAccess.item, 0.8);

            pManager.AddNumberParameter(
                "Remote Stands per Bus Gate", "RPB",
                "Remote stands one bus gate serves in active use.",
                GH_ParamAccess.item, 2.0);

            pManager.AddNumberParameter(
                "Max ÷ Active Stands", "MAX",
                "All stands including overnight parking, divided by the active stands.",
                GH_ParamAccess.item, 1.35);

            pManager.AddNumberParameter(
                "Seated Space m2", "SS",
                "Space for one seated passenger, m2.",
                GH_ParamAccess.item, 1.7);

            pManager.AddNumberParameter(
                "Standing Space m2", "TS",
                "Space for one standing passenger, m2.",
                GH_ParamAccess.item, 1.2);

            pManager.AddNumberParameter(
                "Boarding Queue Space m2", "QS",
                "Space for one passenger waiting to board, m2.",
                GH_ParamAccess.item, 1.2);

            pManager.AddNumberParameter(
                "Next Seat Allowance %", "NSA%",
                "Extra seated space for passengers putting belongings on the next seat, as a percentage.",
                GH_ParamAccess.item, 0.0);

            pManager.AddNumberParameter(
                "Open Lounge Reduction %", "OLR%",
                "Reduction where a lounge is open to the concourse and borrows its space, as a percentage.",
                GH_ParamAccess.item, 0.0);

            pManager.AddIntegerParameter(
                "Podiums per Lounge", "POD",
                "Boarding podiums in one lounge.",
                GH_ParamAccess.item, 1);

            pManager.AddNumberParameter(
                "Podium Area each m2", "PODA",
                "Area of one boarding podium, m2.",
                GH_ParamAccess.item, 0.0);

            pManager.AddTextParameter(
                "Bus Lounge Code", "BLC",
                "The code a bus gate's lounge is sized as. A bus gate serves whatever it serves, so it is sized as the smallest lounge.",
                GH_ParamAccess.item, "C");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Gate Standards", "STD",
                "The gate standards object.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double buffer = 10.0;
            double spares = 0.0;
            double loadFactor = 0.85;
            List<string> swingCodes = new List<string>();
            List<int> swingStands = new List<int>();
            double contactDom = 0.8;
            double contactSwing = 0.8;
            double contactIntl = 0.8;
            double remotePerBus = 2.0;
            double maxRatio = 1.35;
            double seatedSpace = 1.7;
            double standingSpace = 1.2;
            double queueSpace = 1.2;
            double nextSeat = 0.0;
            double openReduction = 0.0;
            int podiums = 1;
            double podiumArea = 0.0;
            string busCode = "C";

            DA.GetData(0, ref buffer);
            DA.GetData(1, ref spares);
            DA.GetData(2, ref loadFactor);
            DA.GetDataList(3, swingCodes);
            DA.GetDataList(4, swingStands);
            DA.GetData(5, ref contactDom);
            DA.GetData(6, ref contactSwing);
            DA.GetData(7, ref contactIntl);
            DA.GetData(8, ref remotePerBus);
            DA.GetData(9, ref maxRatio);
            DA.GetData(10, ref seatedSpace);
            DA.GetData(11, ref standingSpace);
            DA.GetData(12, ref queueSpace);
            DA.GetData(13, ref nextSeat);
            DA.GetData(14, ref openReduction);
            DA.GetData(15, ref podiums);
            DA.GetData(16, ref podiumArea);
            DA.GetData(17, ref busCode);

            GateStandards standards = new GateStandards
            {
                BufferMinutes = Math.Max(0.0, buffer),
                SparesPct = Math.Max(0.0, spares),
                LoadFactor = Math.Min(1.0, Math.Max(0.0, loadFactor)),
                ContactPctDomestic = Math.Min(1.0, Math.Max(0.0, contactDom)),
                ContactPctSwing = Math.Min(1.0, Math.Max(0.0, contactSwing)),
                ContactPctInternational = Math.Min(1.0, Math.Max(0.0, contactIntl)),
                RemoteStandsPerBusGate = Math.Max(0.0, remotePerBus),
                MaxStandsRatio = Math.Max(0.0, maxRatio),
                SeatedSpacePerPax = Math.Max(0.0, seatedSpace),
                StandingSpacePerPax = Math.Max(0.0, standingSpace),
                BoardingQueueSpacePerPax = Math.Max(0.0, queueSpace),
                NextSeatAllowancePct = Math.Max(0.0, nextSeat),
                OpenLoungeReductionPct = Math.Min(1.0, Math.Max(0.0, openReduction)),
                PodiumsPerLounge = Math.Max(0, podiums),
                PodiumAreaEach = Math.Max(0.0, podiumArea),
                BusLoungeCode = string.IsNullOrWhiteSpace(busCode) ? "C" : busCode.Trim()
            };

            int swingCount = Math.Min(swingCodes.Count, swingStands.Count);

            for (int i = 0; i < swingCount; i++)
            {
                string code = swingCodes[i];

                if (string.IsNullOrWhiteSpace(code))
                    continue;

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

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("6C2E9A17-4B58-4D31-9F0A-7E15C8B3D642"); }
        }
    }
}
