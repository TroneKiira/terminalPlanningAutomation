using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;

using AirportPlanning.Core.Models;


namespace AirportPlanning.Components.PassengerAnalysis
{
    public class PassengerFilterComponent : GH_Component
    {
        public PassengerFilterComponent()
          : base("Passenger Filter", "FILTER",
            "Splits a passenger population into separate demographic flows for spatial routing.",
            "Airport Planning", "Analysis")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "The mixed passenger population.", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Domestic", "DOM", "Passengers on domestic flights.", GH_ParamAccess.list);
            pManager.AddGenericParameter("International", "INT", "Passengers on international flights.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Male", "M", "Male passengers.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Female", "F", "Female passengers.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Checked Bags", "BAG", "Passengers with checked baggage.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Cabin Bag Only", "CAB", "Passengers with only cabin baggage.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> passengers = new List<Passenger>();

            if (!DA.GetDataList(0, passengers)) return;

            // Filter the lists
            var domestic = passengers.Where(p => p.PassengerType == PassengerType.Domestic).ToList();
            var international = passengers.Where(p => p.PassengerType == PassengerType.International).ToList();

            var male = passengers.Where(p => p.Gender == PassengerGender.Male).ToList();
            var female = passengers.Where(p => p.Gender == PassengerGender.Female).ToList();

            var checkedBags = passengers.Where(p => p.HasCheckedBag).ToList();
            var cabinBags = passengers.Where(p => !p.HasCheckedBag).ToList();

            // Output the split flows
            DA.SetDataList(0, domestic);
            DA.SetDataList(1, international);
            DA.SetDataList(2, male);
            DA.SetDataList(3, female);
            DA.SetDataList(4, checkedBags);
            DA.SetDataList(5, cabinBags);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("FF998877-6655-4433-2211-00AABBCCDDEE"); } }
    }
}