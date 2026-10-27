using AirportPlanning.Core.Area;
using AirportPlanning.Core.Gates;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AirportPlanning.Components.FacilitySizingComponents
{
    /// <summary>
    /// Sizes the aircraft stands and the terminal gate lounges.
    ///
    /// A STAND IS APRON AND A LOUNGE IS TERMINAL, so the two are counted
    /// separately: the stands are numbers of parking positions on the apron,
    /// and only the lounges are floor area.
    ///
    /// The stands follow the ICAO peak-hour method - movements multiplied by
    /// how long each aircraft holds a stand - and the lounges are sized on
    /// the planeload the aircraft brings.
    /// </summary>
    public class GateSizerComponent : GH_Component
    {
        public GateSizerComponent()
          : base("Gate & Stand Sizer", "G-SIZE",
            "Sizes aircraft stands from peak-hour movements and the terminal gate lounges from the planeload each aircraft brings.",
            "Airport Planning", "Facility Sizing")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Gate Inputs", "GIN",
                "One row per aircraft type, from the Gate Inputs component.",
                GH_ParamAccess.list);
            pManager[0].Optional = true;

            pManager.AddGenericParameter(
                "Gate Standards", "G-STD",
                "The gate standards.",
                GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddGenericParameter(
                "Area Standards", "A-STD",
                "Optional: Facility Area Standards. Supplies the circulation, MEP and structure " +
                "allowances applied to the lounge area.",
                GH_ParamAccess.item);
            pManager[2].Optional = true;

            pManager.AddNumberParameter(
                "Expansion Factor", "EF",
                "Optional: uplift applied to the lounge area before the allowances, as a fraction " +
                "(0.10 = 10%). 0 leaves it alone.",
                GH_ParamAccess.item, 0.0);
            pManager[3].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Gate Sizing Result", "GSR",
                "The complete stand and lounge result.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Aircraft Stands", "ST",
                "Stands in use at the peak, across every group.",
                GH_ParamAccess.item);
            pManager.AddIntegerParameter(
                "Contact Stands", "CS",
                "Stands with a bridge attached.",
                GH_ParamAccess.item);
            pManager.AddIntegerParameter(
                "Bus Gates", "BG",
                "Stands served by a bus gate.",
                GH_ParamAccess.item);
            pManager.AddIntegerParameter(
                "Remote Stands", "RS",
                "Remote stands the bus gates use.",
                GH_ParamAccess.item);
            pManager.AddIntegerParameter(
                "Max Stands", "MS",
                "All stands, including overnight parking.",
                GH_ParamAccess.item);
            pManager.AddIntegerParameter(
                "Net Remote Stands", "NRS",
                "Max stands less the contact stands.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Gate Lounges", "GL",
                "Number of gate lounges.",
                GH_ParamAccess.item);
            pManager.AddNumberParameter(
                "Hold Area", "HA",
                "Hold room area of every lounge (sqm).",
                GH_ParamAccess.item);
            pManager.AddNumberParameter(
                "Boarding Queue Area", "BQA",
                "Boarding queue area of every lounge (sqm).",
                GH_ParamAccess.item);
            pManager.AddNumberParameter(
                "Podium Area", "PA",
                "Boarding podium area of every lounge (sqm).",
                GH_ParamAccess.item);
            pManager.AddNumberParameter(
                "Net Lounge Area", "NET",
                "Lounge area before the allowances (sqm).",
                GH_ParamAccess.item);
            pManager.AddNumberParameter(
                "Gross Lounge Area", "GROSS",
                "Total buildable lounge area (sqm).",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Stand Breakdown", "SBD",
                "One line per group and code.",
                GH_ParamAccess.list);
            pManager.AddTextParameter(
                "Lounge Breakdown", "LBD",
                "One line per lounge kind.",
                GH_ParamAccess.list);
            pManager.AddTextParameter(
                "Warnings", "WARN",
                "Anything to check in this result.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GateCodeInput> inputs = new List<GateCodeInput>();
            GateStandards standards = null;
            FacilityAreaStandardSet areaStandards = null;
            double expansionFactor = 0.0;

            DA.GetDataList(0, inputs);
            DA.GetData(1, ref standards);
            DA.GetData(2, ref areaStandards);
            DA.GetData(3, ref expansionFactor);

            if (standards == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "No Gate Standards received. Connect the Gate Standards component.");

                return;
            }

            if (inputs == null || inputs.Count == 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "No aircraft types received, so no stands or lounges were sized.");

                return;
            }

            GateSizingResult result = GateCalculator.Calculate(inputs, standards);

            double net = result.NetArea;

            if (expansionFactor > 0.0)
                net *= (1.0 + expansionFactor);

            double circulation = areaStandards != null ? areaStandards.CirculationPercent : 0.0;
            double mep = areaStandards != null ? areaStandards.MepPercent : 0.0;
            double structure = areaStandards != null ? areaStandards.StructurePercent : 0.0;

            GrossArea gross = AreaGrossUp.Calculate(net, circulation, mep, structure);

            List<string> standLines = DescribeStands(result);
            List<string> loungeLines = DescribeLounges(result);

            DA.SetData(0, result);
            DA.SetData(1, result.ActiveTotal);
            DA.SetData(2, result.ContactTotal);
            DA.SetData(3, result.BusTotal);
            DA.SetData(4, result.RemoteActive);
            DA.SetData(5, result.MaxStands);
            DA.SetData(6, result.NetRemote);
            DA.SetData(7, result.LoungeCount);
            DA.SetData(8, result.HoldArea);
            DA.SetData(9, result.QueueArea);
            DA.SetData(10, result.PodiumArea);
            DA.SetData(11, gross.Net);
            DA.SetData(12, gross.Total);
            DA.SetDataList(13, standLines);
            DA.SetDataList(14, loungeLines);
            DA.SetDataList(15, result.Warnings);

            foreach (string warning in result.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }

        private static List<string> DescribeStands(GateSizingResult result)
        {
            List<string> lines = new List<string>();

            foreach (GateBucketStands bucket in result.Buckets)
            {
                foreach (GateCodeStands code in bucket.Codes)
                {
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} {1}: {2} active ({3} contact, {4} bus){5}",
                        bucket.Bucket,
                        code.Code,
                        code.Rounded,
                        code.Contact,
                        code.Bus,
                        code.Swing > 0 ? " [swing]" : string.Empty));
                }

                if (bucket.SplitExtra != 0)
                {
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: the per-code split is {1} stand(s) over the sector total of {2}",
                        bucket.Bucket,
                        bucket.SplitExtra,
                        bucket.SectorTotal));
                }
            }

            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Active {0} = {1} contact + {2} bus; remote {3}; max {4}; net remote {5}",
                result.ActiveTotal,
                result.ContactTotal,
                result.BusTotal,
                result.RemoteActive,
                result.MaxStands,
                result.NetRemote));

            return lines;
        }

        private static List<string> DescribeLounges(GateSizingResult result)
        {
            List<string> lines = new List<string>();

            foreach (GateLounge lounge in result.Lounges)
            {
                lines.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1}: {2} lounge(s) x {3:0} m2 = {4:0} m2 (hold {5:0}, queue {6:0}, podium {7:0})",
                    lounge.Bucket,
                    lounge.Code,
                    lounge.Count,
                    lounge.AreaEach,
                    lounge.Area,
                    lounge.HoldArea,
                    lounge.QueueArea,
                    lounge.PodiumArea));
            }

            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Lounges {0}: hold {1:0} + queue {2:0} + podium {3:0} = {4:0} m2",
                result.LoungeCount,
                result.HoldArea,
                result.QueueArea,
                result.PodiumArea,
                result.NetArea));

            return lines;
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("2D5A8F31-9C47-4B60-A3D8-71C5E0B94F62"); }
        }
    }
}
