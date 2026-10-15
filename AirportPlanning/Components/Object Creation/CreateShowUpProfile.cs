using AirportPlanning.Core.Inputs;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace AirportPlanning.Components.Object_Creation
{
    /// <summary>
    /// Builds the show-up profile for a passenger type: how its people spread
    /// across the hour before their flight, as ten six-minute bands.
    ///
    /// Leisure, Business and Mix carry their own published band values.
    /// Custom takes the familiar W15/W30/W60 window shares and fits a Gaussian
    /// through them, reading the ten bands off that curve - so a planner never
    /// has to think in bands unless they want to.
    ///
    /// W15/W30/W60 are the nested windows ending at departure: the share
    /// arriving in the last 15, 30 and 60 minutes. W60 is always 100%.
    /// </summary>
    public class CreateShowUpProfile : GH_Component
    {
        private const string CustomPreset = "Custom";

        public CreateShowUpProfile()
          : base("Show-Up Profile", "SUP",
            "Arrival show-up curve for a passenger type across the hour before departure. Leisure, Business and Mix presets, or a custom W15/W30/W60 fit.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Preset", "P",
                "Leisure, Business, Mix, or Custom. Custom builds the curve from the W15/W30/W60 inputs.",
                GH_ParamAccess.item, "Mix");

            pManager.AddTextParameter(
                "Name", "N",
                "Label for this curve in the chart legend.",
                GH_ParamAccess.item, "");
            pManager[1].Optional = true;

            pManager.AddNumberParameter(
                "W15", "W15",
                "Custom only: share of the hour arriving in the last 15 minutes before departure, as a percentage.",
                GH_ParamAccess.item, 35.0);
            pManager[2].Optional = true;

            pManager.AddNumberParameter(
                "W30", "W30",
                "Custom only: share of the hour arriving in the last 30 minutes before departure, as a percentage.",
                GH_ParamAccess.item, 55.0);
            pManager[3].Optional = true;

            pManager.AddNumberParameter(
                "W60", "W60",
                "Custom only: share of the hour arriving in the last 60 minutes. Always 100%.",
                GH_ParamAccess.item, 100.0);
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Show-Up Profile", "SUP",
                "Show-up profile to wire into a Passenger Profile.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Band Shares", "BS",
                "Share of the hour arriving in each of the ten six-minute bands, earliest first (the band covering 54-60 minutes before departure is first).",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "Band Windows", "BW",
                "Start minute before departure for each band, matching Band Shares order.",
                GH_ParamAccess.list);

            pManager.AddNumberParameter(
                "W15", "o15",
                "Resolved share arriving in the last 15 minutes before departure (%).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "W30", "o30",
                "Resolved share arriving in the last 30 minutes before departure (%).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "W60", "o60",
                "Resolved share arriving in the last 60 minutes before departure (%).",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Validation", "V",
                "Validation messages. Empty when the profile is valid.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string preset = "Mix";
            string name = "";
            double w15 = 35.0;
            double w30 = 55.0;
            double w60 = 100.0;

            if (!DA.GetData(0, ref preset))
                return;

            DA.GetData(1, ref name);
            DA.GetData(2, ref w15);
            DA.GetData(3, ref w30);
            DA.GetData(4, ref w60);

            ShowUpProfile profile;

            if (string.IsNullOrWhiteSpace(preset) ||
                preset.Trim().Equals(CustomPreset, StringComparison.OrdinalIgnoreCase))
            {
                profile = ShowUpProfile.FromWindowShares(
                    string.IsNullOrWhiteSpace(name) ? "Custom" : name.Trim(),
                    w15 / 100.0,
                    w30 / 100.0,
                    w60 / 100.0);
            }
            else
            {
                profile = ShowUpProfile.FromPreset(preset);

                if (profile == null)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        $"Unknown preset '{preset}'. Use Leisure, Business, Mix or Custom.");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(name))
                    profile.Name = name.Trim();
            }

            List<string> errors = profile.GetValidationErrors();

            foreach (string error in errors)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);

            List<double> windows = new List<double>();

            for (int i = 0; i < profile.Bands.Count; i++)
                windows.Add(profile.GetBandStartMinutesBeforeDeparture(i));

            DA.SetData(0, profile);
            DA.SetDataList(1, profile.Bands);
            DA.SetDataList(2, windows);
            DA.SetData(3, profile.W15 * 100.0);
            DA.SetData(4, profile.W30 * 100.0);
            DA.SetData(5, profile.W60 * 100.0);
            DA.SetDataList(6, errors);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("D3E4F5A6-1728-493A-BC4D-5E6F70819203"); }
        }
    }
}
