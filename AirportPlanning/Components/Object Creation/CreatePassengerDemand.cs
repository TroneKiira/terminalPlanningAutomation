using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

using AirportPlanning.Core.Inputs;

namespace AirportPlanning.Components.Object_Creation
{
    public class CreatePassengerDemand : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PassengerDemand class.
        /// </summary>
        public CreatePassengerDemand()
          : base("Passenger Demand", "PD",
        "Defines the passenger demand used for airport planning and simulation.",
        "Airport Planning", "Object Creation")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter(
        "Design Hour Demand",
        "DHD",
        "Total passenger demand during the airport design hour.",
        GH_ParamAccess.item);

            pManager.AddNumberParameter(
                "Departure Percentage",
                "DEP%",
                "Percentage of the design hour demand that represents departing passengers.",
                GH_ParamAccess.item,
                50.0);

            pManager.AddNumberParameter(
                "Domestic Percentage",
                "DOM%",
                "Percentage of departing passengers travelling on domestic flights.",
                GH_ParamAccess.item,
                70.0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter(
            "Passenger Demand",
            "PD",
            "Passenger demand definition used for passenger generation and terminal planning.",
            GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int designHourDemand = 0;

            double departurePercentage = 50.0;
            double domesticPercentage = 70.0;

            if (!DA.GetData(0, ref designHourDemand))
                return;

            if (!DA.GetData(1, ref departurePercentage))
                return;

            if (!DA.GetData(2, ref domesticPercentage))
                return;

            int departurePassengers =
                (int)Math.Round(
                    designHourDemand *
                    departurePercentage / 100.0);

            int domesticPassengers =
                (int)Math.Round(
                    departurePassengers *
                    domesticPercentage / 100.0);

            int internationalPassengers =
                departurePassengers -
                domesticPassengers;

            PassengerDemand demand =
                new PassengerDemand
                {
                    DesignHourDemand = designHourDemand,

                    DeparturePercentage = departurePercentage,
                    DomesticPercentage = domesticPercentage,

                    
                };

            DA.SetData(
                0,
                demand);
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
            get { return new Guid("7A87E672-AA75-4B25-823D-1695E4E186C1"); }
        }
    }
}