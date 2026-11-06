using AirportPlanning.Core.Inputs;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace AirportPlanning.Components.Object_Creation
{
    public class CreatePassengerDemand : GH_Component
    {
        public CreatePassengerDemand()
          : base("Create Passenger Demand", "CPD",
            "Creates a Design Hour Profile (DHP) for a departure passenger type " +
            "(Domestic Departure or International Departure). Arrival demand is " +
            "supplied by the Deplaning Profile route.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "DHP Type", "T",
                "0 = Domestic Departure, 2 = International Departure. " +
                "Arrival types (1, 3) are rejected: arrival demand uses the Deplaning Profile.",
                GH_ParamAccess.item, 0);

            pManager.AddIntegerParameter(
                "Design Hour Demand", "DHD",
                "Total passenger demand during the design hour (the 100% reference).",
                GH_ParamAccess.item, 4000);

            pManager.AddNumberParameter(
                "Demand Factors", "F%",
                "Demand factor for each hour as a percentage of the DHD hourly rate. One value per hour of the window. 100% = design hour; Demand Basis controls whether this is terminal arrivals or flights by STD/STA.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Analysis Period", "AP",
                "Total analysis window in hours (e.g. 5). The window is always split into one-hour steps, so supply one Demand Factor per hour.",
                GH_ParamAccess.item, 5);

            pManager.AddIntegerParameter(
                "Design Hour Index", "DH",
                "Zero-based index of the time step that represents the design hour.",
                GH_ParamAccess.item, 2);

            pManager.AddBooleanParameter(
                "Warm-Up", "WU",
                "Legacy Terminal Arrival basis only: when true, a synthetic pre-window warm-up segment is simulated so facilities do not start empty. Flight Hour basis does not generate synthetic warm-up passengers.",
                GH_ParamAccess.item, true);

            pManager.AddIntegerParameter(
                "Warm-Up Duration", "WUD",
                "Legacy Terminal Arrival basis only: duration of the synthetic warm-up segment in minutes.",
                GH_ParamAccess.item, 60);

            pManager.AddNumberParameter(
                "Warm-Up Factor", "WUF",
                "Legacy Terminal Arrival basis only: warm-up demand factor as a percentage of the DHD hourly rate. Flight Hour basis does not synthesize warm-up demand.",
                GH_ParamAccess.item, 50.0);

            pManager.AddIntegerParameter(
                "Demand Basis", "DB",
                "0 = legacy Terminal Arrival demand (factor means passengers arriving in that hour); 1 = Flight Hour demand (factor means passengers on flights by STD/STA). Defaults to Flight Hour for this component.",
                GH_ParamAccess.item, 1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "DHP", "D",
                "Design Hour Profile for the selected passenger type.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int type = 0;
            int dhd = 4000;
            List<double> factors = new List<double>();
            int period = 5;
            int designIndex = 2;
            bool warmUp = true;
            int warmUpMinutes = 60;
            double warmUpFactor = 50.0;
            int demandBasis = (int)DemandTimeBasis.FlightHour;

            if (!DA.GetData(0, ref type)) return;
            if (!DA.GetData(1, ref dhd)) return;
            DA.GetDataList(2, factors);
            if (!DA.GetData(3, ref period)) return;
            if (!DA.GetData(4, ref designIndex)) return;
            if (!DA.GetData(5, ref warmUp)) return;
            if (!DA.GetData(6, ref warmUpMinutes)) return;
            if (!DA.GetData(7, ref warmUpFactor)) return;
            if (!DA.GetData(8, ref demandBasis)) return;

            if (type == (int)DHPType.DomesticArrival ||
                type == (int)DHPType.InternationalArrival)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"DHP Type {type} is an arrival type. Passenger generators are " +
                    "departures-only; use the Deplaning Profile for arrival demand.");
                return;
            }

            DHPType dhpType = type == (int)DHPType.InternationalDeparture
                ? DHPType.InternationalDeparture
                : DHPType.DomesticDeparture;

            DHP dhp = new DHP
            {
                Type = dhpType,
                DemandBasis = (DemandTimeBasis)Math.Max(
                    (int)DemandTimeBasis.TerminalArrival,
                    Math.Min((int)DemandTimeBasis.FlightHour, demandBasis)),
                DesignHourDemand = dhd,
                DemandFactors = factors != null ? new List<double>(factors) : new List<double>(),
                AnalysisPeriodHours = period,
                DesignHourIndex = designIndex,
                WarmUpEnabled = warmUp,
                WarmUpMinutes = warmUpMinutes,
                WarmUpFactor = warmUpFactor
            };

            List<string> errors = dhp.GetValidationErrors();

            foreach (string error in errors)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);

            DA.SetData(0, dhp);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("64A882C3-5305-411D-9022-3CE3B306A5C7"); }
        }
    }
}
