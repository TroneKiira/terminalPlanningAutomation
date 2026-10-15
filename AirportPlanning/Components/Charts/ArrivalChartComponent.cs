using AirportPlanning.Core.Charts;
using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using Grasshopper.Kernel;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace AirportPlanning.Components.Charts
{
    /// <summary>
    /// Plots generated demand. Three views:
    ///   Demand Profile - per-minute arrivals across the window;
    ///   By Type       - the same, split by passenger type;
    ///   Show-Up Curve - the Leisure / Business / Mix curves themselves, so the
    ///                  input shapes can be compared directly.
    /// </summary>
    public class ArrivalChartComponent : GH_Component
    {
        public ArrivalChartComponent()
          : base("Arrival Chart", "ArrChart",
            "Plots generated demand: per-minute arrivals across the analysis window, splits by passenger type, or the show-up curves themselves.",
            "Airport Planning", "Charts")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Arrival Simulations", "AS",
                "Arrival Simulations from the Windowed Passenger Generator.",
                GH_ParamAccess.list);
            pManager[0].Optional = true;

            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "Passengers from the generator. Required for the by-type view.",
                GH_ParamAccess.list);
            pManager[1].Optional = true;

            pManager.AddGenericParameter(
                "Show-Up Profiles", "SUP",
                "Show-Up Profiles to compare, from the Show-Up Profile component. Required for the Show-Up Curve view.",
                GH_ParamAccess.list);
            pManager[2].Optional = true;

            pManager.AddTextParameter(
                "Title", "T",
                "Chart title. Leave empty for the default.",
                GH_ParamAccess.item, "");
            pManager[3].Optional = true;

            pManager.AddTextParameter(
                "View", "V",
                "Demand Profile, By Type, or Show-Up Curve.",
                GH_ParamAccess.item, "Demand Profile");
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Chart", "C",
                "Rendered chart bitmap.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<ArrivalSimulationResult> simulations = new List<ArrivalSimulationResult>();
            List<Passenger> passengers = new List<Passenger>();
            List<ShowUpProfile> showUps = new List<ShowUpProfile>();
            string title = "";
            string view = "Demand Profile";

            DA.GetDataList(0, simulations);
            DA.GetDataList(1, passengers);
            DA.GetDataList(2, showUps);
            DA.GetData(3, ref title);
            DA.GetData(4, ref view);

            string mode = (view ?? "").Trim().ToLowerInvariant();

            PlotModel model;

            if (mode == "show-up curve")
            {
                if (showUps == null || showUps.Count == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        "The Show-Up Curve view needs at least one Show-Up Profile input.");
                    return;
                }

                model = ChartRenderer.CreateShowUpCurveModel(
                    showUps,
                    string.IsNullOrWhiteSpace(title) ? "Show-Up Curves by Passenger Mix" : title);
            }
            else if (mode == "by type")
            {
                if (passengers == null || passengers.Count == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        "The By Type view needs a Passengers input.");
                    return;
                }

                model = ChartRenderer.CreateArrivalProfileModel(
                    passengers,
                    string.IsNullOrWhiteSpace(title) ? "Arrivals by Passenger Type" : title);
            }
            else
            {
                if (simulations == null || simulations.Count == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        "No arrival simulations received. Connect the Windowed Passenger Generator's Arrival Simulations output.");
                    return;
                }

                model = ChartRenderer.CreateArrivalDemandModel(
                    simulations,
                    string.IsNullOrWhiteSpace(title) ? "Arrival Demand Profile" : title);
            }

            Bitmap chart = ChartRenderer.ExportToBitmap(model);

            if (chart == null)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    ChartRenderer.CanRender
                        ? "Chart could not be rendered."
                        : "Chart rendering is not available on this platform: charts need the Windows build of the plugin.");
                return;
            }

            DA.SetData(0, chart);
        }

        protected override Bitmap Icon { get { return null; } }

        public override Guid ComponentGuid
        {
            get { return new Guid("B2C3D4E5-F6A7-8901-BCDE-F12345678901"); }
        }
    }
}
