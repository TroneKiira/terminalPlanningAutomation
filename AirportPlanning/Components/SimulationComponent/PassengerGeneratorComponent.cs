using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Walking;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace AirportPlanning.Components.SimulationComponent
{
    public class PassengerGeneratorComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PassengerGeneratorComponent class.
        /// </summary>
        public PassengerGeneratorComponent()
          : base("Passenger Generator", "PG",
        "Generates a passenger population for simulation.",
        "Airport Planning", "Simulation")
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
        "Passenger Demand",
        "PD",
        "Passenger demand definition for the simulation.",
        GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Domestic Profiles",
                "DP",
                "Passenger profiles describing domestic passenger behaviour.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "International Profiles",
                "IP",
                "Passenger profiles describing international passenger behaviour.",
                GH_ParamAccess.list);

            pManager.AddIntegerParameter(
                "Departure Minute",
                "DM",
                "Scheduled departure time expressed in minutes from the simulation origin.",
                GH_ParamAccess.item);

            pManager.AddIntegerParameter(
                "Simulation Horizon",
                "H",
                "Time window before departure over which passengers are generated (minutes).",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Walking Profile",
                "P",
                "Walking Profile for Different Kinds of passengers",
                GH_ParamAccess.item
                );

            pManager.AddIntegerParameter(
                "Random Seed",
                "S",
                "Random number seed used to ensure repeatable passenger generation.",
                GH_ParamAccess.item,
                1);
            
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers",
                "PAX",
                "Generated passenger population ordered by arrival time.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Passenger Generation Result",
                "PGR",
                "Complete passenger generation results.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter(
                "Arrival Simulations",
                "AS",
                "Arrival simulations generated for each passenger profile.",
                GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            PassengerDemand demand =
        null;

            List<PassengerProfile> domesticProfiles =
                new List<PassengerProfile>();

            List<PassengerProfile> internationalProfiles =
                new List<PassengerProfile>();
            WalkingProfile walkingProfile=null;

            int departureMinute = 0;
            int simulationHorizon = 0;
            int seed = 1;

            if (!DA.GetData(0, ref demand))
                return;

            if (!DA.GetDataList(1, domesticProfiles))
                return;

            if (!DA.GetDataList(2, internationalProfiles))
                return;

            if (!DA.GetData(3, ref departureMinute))
                return;
            if (!DA.GetData(4, ref simulationHorizon))
                return;

            if (!DA.GetData(5, ref walkingProfile))
                return;
           

            if (!DA.GetData(6, ref seed))
                return;

            PassengerGenerator generator =
                new PassengerGenerator();

            PassengerGenerationResult result =
                generator.Generate(
                    demand,
                    domesticProfiles,
                    internationalProfiles,
                    walkingProfile,
                    departureMinute,
                    simulationHorizon,
                    seed);

            DA.SetDataList(
                0,
                result.Passengers);

            DA.SetData(
                1,
                result);

            DA.SetDataList(
                2,
                result.ArrivalSimulations);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("8D05DCEE-4931-4D05-8C89-63A00791F5AA"); }
        }
    }
}