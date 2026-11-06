using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

using AirportPlanning.Core.Inputs;

namespace AirportPlanning.Components.Object_Creation
{
    /// <summary>
    /// Builds an arrival deplaning profile: the share of the landed peak hour
    /// reaching the first arrival process in its busiest 15 / 30 / 60 minutes.
    ///
    /// This is the profile-based simulation route. Auto selects Small, Medium
    /// or Large from annual passenger volume; PHP (peak-hour passengers) is
    /// supplied by the user or the schedule and merely scales the chosen
    /// shares into peak-window passenger counts. The flight-linked DES remains
    /// the alternative route.
    /// </summary>
    public class CreateDeplaningProfile : GH_Component
    {
        public CreateDeplaningProfile()
          : base("Deplaning Profile", "DEP",
            "Arrival deplaning profile. Auto selects Small/Medium/Large from annual passenger volume; " +
            "PHP (peak-hour passengers, supplied by the user or schedule-derived from Flight Schedule Deconstruct) " +
            "scales the chosen shares into peak-window passenger counts. " +
            "Custom takes W15 and W30 with W60 fixed at 100%.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter(
                "Mode", "M",
                "Auto, Small, Medium, Large, or Custom. Auto selects the category from Annual MPPA.",
                GH_ParamAccess.item, "Auto");

            pManager.AddNumberParameter(
                "Annual MPPA", "MPPA",
                "Annual terminal passenger volume in millions. Used only by Auto: " +
                "Small <= 10, Medium > 10 to <= 25, Large > 25.",
                GH_ParamAccess.item, 25.0);

            pManager.AddNumberParameter(
                "PHP", "PHP",
                "Peak-hour passengers, supplied by the user or from the schedule " +
                "(Flight Schedule Deconstruct output PHP). " +
                "Scales the profile shares into peak-window passenger counts; it does not select the category.",
                GH_ParamAccess.item, 1000.0);

            pManager.AddTextParameter(
                "Name", "N",
                "Name for a Custom profile (ignored by the preset modes).",
                GH_ParamAccess.item, "Custom");
            pManager[3].Optional = true;

            pManager.AddNumberParameter(
                "W15", "W15",
                "Custom only: share of the peak hour in its busiest 15 minutes (%).",
                GH_ParamAccess.item, 40.0);
            pManager[4].Optional = true;

            pManager.AddNumberParameter(
                "W30", "W30",
                "Custom only: share of the peak hour in its busiest 30 minutes (%). Minimum 50%.",
                GH_ParamAccess.item, 60.0);
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Deplaning Profile", "DEP",
                "Deplaning profile carrying the resolved W15 / W30 / W60 shares.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Selected Category", "CAT",
                "The category actually used: Small, Medium, Large (including an Auto selection) or Custom.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "W15", "o15",
                "Resolved share of the peak hour in its busiest 15 minutes (%).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "W30", "o30",
                "Resolved share of the peak hour in its busiest 30 minutes (%).",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "W60", "o60",
                "Resolved share of the peak hour in its busiest 60 minutes (%). Always 100%.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Peak 15-min Passengers", "P15",
                "PHP x W15: passengers in the busiest 15 minutes.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Peak 30-min Passengers", "P30",
                "PHP x W30: passengers in the busiest 30 minutes.",
                GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Peak 60-min Passengers", "P60",
                "PHP x W60: passengers in the peak hour.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Validation", "V",
                "Validation messages. Empty when the profile is valid.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Source", "SRC",
                "How the profile was chosen: Auto from annual volume, a fixed category, or manual custom values.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string modeText = "Auto";
            double targetMppa = 25.0;
            double php = 1000.0;
            string name = "Custom";
            double w15 = 40.0;
            double w30 = 60.0;

            if (!DA.GetData(0, ref modeText)) return;
            DA.GetData(1, ref targetMppa);
            DA.GetData(2, ref php);
            DA.GetData(3, ref name);
            DA.GetData(4, ref w15);
            DA.GetData(5, ref w30);

            DeplaningProfileMode mode;

            if (!DeplaningProfile.TryParseMode(modeText, out mode))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    $"Unknown mode '{modeText}'. Use Auto, Small, Medium, Large or Custom.");
                return;
            }

            if (double.IsNaN(php) || double.IsInfinity(php) || php < 0.0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "PHP must be a non-negative number of peak-hour passengers.");
                return;
            }

            bool hasMppa =
                !double.IsNaN(targetMppa) &&
                !double.IsInfinity(targetMppa) &&
                targetMppa > 0.0;

            if (mode == DeplaningProfileMode.Auto && !hasMppa)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "No usable annual passenger volume supplied; Auto fell back to Medium.");
            }

            DeplaningProfile profile = DeplaningProfile.FromMode(
                mode,
                targetMppa,
                name,
                w15 / 100.0,
                w30 / 100.0);

            string category = DeplaningProfile.CategoryForMode(mode, targetMppa);

            string source;

            switch (mode)
            {
                case DeplaningProfileMode.Auto:
                    source = $"Auto selected {category} from {targetMppa:0.##} mppa";
                    break;
                case DeplaningProfileMode.Custom:
                    source = "Manual custom W15 / W30 values";
                    break;
                default:
                    source = $"Fixed {category} category";
                    break;
            }

            List<string> errors = profile.GetValidationErrors();

            foreach (string error in errors)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);

            DA.SetData(0, profile);
            DA.SetData(1, category);
            DA.SetData(2, profile.W15 * 100.0);
            DA.SetData(3, profile.W30 * 100.0);
            DA.SetData(4, profile.W60 * 100.0);
            DA.SetData(5, profile.Peak15Passengers(php));
            DA.SetData(6, profile.Peak30Passengers(php));
            DA.SetData(7, profile.Peak60Passengers(php));
            DA.SetDataList(8, errors);
            DA.SetData(9, source);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("E4F5A6B7-2839-4A4B-CD5E-6F7081920314"); }
        }
    }
}
