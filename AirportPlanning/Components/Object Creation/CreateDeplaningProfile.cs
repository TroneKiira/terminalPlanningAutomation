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
    /// DATA MODEL ONLY — reserved for arrival-side facilities. No sizing
    /// component consumes it yet.
    /// </summary>
    public class CreateDeplaningProfile : GH_Component
    {
        public CreateDeplaningProfile()
          : base("Deplaning Profile", "DEP",
            "Defines an arrival deplaning curve (busiest 15 / 30 / 60 min shares of the landed peak hour). Reserved for arrival-side facilities; not yet consumed.",
            "Airport Planning", "Object Creation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter(
                "Preset", "P",
                "0 = Custom (use the fields below), 1 = Profile 1 (most concentrated), 2 = Profile 2, 3 = Profile 3 (flattest).",
                GH_ParamAccess.item, 0);

            pManager.AddTextParameter(
                "Name", "N",
                "Name for a custom profile (ignored for presets).",
                GH_ParamAccess.item, "Custom");

            pManager.AddNumberParameter(
                "W15", "W15",
                "Share of the landed peak hour in its busiest 15 minutes (%).",
                GH_ParamAccess.item, 25.0);

            pManager.AddNumberParameter(
                "W30", "W30",
                "Share of the landed peak hour in its busiest 30 minutes (%). Minimum 50%.",
                GH_ParamAccess.item, 50.0);

            pManager.AddNumberParameter(
                "W60", "W60",
                "Share of the landed peak hour in its busiest 60 minutes (%). Always 100%.",
                GH_ParamAccess.item, 100.0);

            pManager.AddBooleanParameter(
                "Use Airport Size Preset", "AUTO",
                "Prefill the authored deplaning profile from target annual mppa: " +
                "Small <= 10, Medium <= 25, Large above 25. The shares remain editable afterwards.",
                GH_ParamAccess.item, false);

            pManager.AddNumberParameter(
                "Target Annual MPPA", "MPPA",
                "Target annual passenger throughput in millions, used only when Airport Size Preset is on.",
                GH_ParamAccess.item, 25.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Deplaning Profile", "DEP",
                "Deplaning profile. Reserved for future arrival-side facilities.",
                GH_ParamAccess.item);

            pManager.AddTextParameter(
                "Validation", "V",
                "Validation messages. Empty when the shape is valid.",
                GH_ParamAccess.list);

            pManager.AddTextParameter(
                "Source", "SRC",
                "Whether the preset came from target airport size or was entered manually. " +
                "The report labels the size presets as authored assumptions.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int preset = 0;
            string name = "Custom";
            double w15 = 25.0;
            double w30 = 50.0;
            double w60 = 100.0;
            bool useAirportSize = false;
            double targetMppa = 25.0;

            if (!DA.GetData(0, ref preset)) return;
            DA.GetData(1, ref name);
            DA.GetData(2, ref w15);
            DA.GetData(3, ref w30);
            DA.GetData(4, ref w60);
            DA.GetData(5, ref useAirportSize);
            DA.GetData(6, ref targetMppa);

            string source;
            DeplaningProfile profile;

            if (useAirportSize)
            {
                profile = DeplaningProfile.FromAirportSize(targetMppa);
                source = $"Airport size preset: {profile.Name} (target {targetMppa:0.##} mppa)";

                if (targetMppa <= 0.0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        "No target annual mppa supplied; using the Medium default.");
                }
            }
            else
            {
                profile = DeplaningProfile.FromPreset(preset);
                source = "Manual or fixed preset";

                if (profile == null)
                {
                    string label = string.IsNullOrWhiteSpace(name) ? "Custom" : name.Trim();

                    profile = new DeplaningProfile(
                        label,
                        w15 / 100.0,
                        w30 / 100.0,
                        w60 / 100.0);

                    source = "Manual custom values";
                }
            }

            List<string> errors = profile.GetValidationErrors();

            foreach (string error in errors)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, error);

            DA.SetData(0, profile);
            DA.SetDataList(1, errors);
            DA.SetData(2, source);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("E4F5A6B7-2839-4A4B-CD5E-6F7081920314"); }
        }
    }
}
