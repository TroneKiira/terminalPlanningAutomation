using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;

using AirportPlanning.Core.Analysis;
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
            // Legacy outputs first: their positions are frozen because saved
            // definitions wire them by index.
            pManager.AddGenericParameter("Domestic", "DOM", "Passengers on domestic flights.", GH_ParamAccess.list);
            pManager.AddGenericParameter("International", "INT", "Passengers on international flights.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Male", "M", "Male passengers.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Female", "F", "Female passengers.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Checked Bags", "BAG", "Passengers with checked baggage.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Cabin Bag Only", "CAB", "Passengers with only cabin baggage.", GH_ParamAccess.list);

            // Flight-leg outputs (DHPType): departure or arrival, per sector.
            pManager.AddGenericParameter("Domestic Departures", "DDP", "Domestic departing passengers (DHPType DomesticDeparture).", GH_ParamAccess.list);
            pManager.AddGenericParameter("International Departures", "IDP", "International departing passengers (DHPType InternationalDeparture).", GH_ParamAccess.list);
            pManager.AddGenericParameter("Domestic Arrivals", "DAP", "Domestic arriving passengers (DHPType DomesticArrival).", GH_ParamAccess.list);
            pManager.AddGenericParameter("International Arrivals", "IAP", "International arriving passengers (DHPType InternationalArrival).", GH_ParamAccess.list);
            pManager.AddGenericParameter("Departures", "DEP", "All departing passengers, domestic and international.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Arrivals", "ARR", "All arriving passengers, domestic and international.", GH_ParamAccess.list);

            // Journey-role outputs (Journey): where the passenger is going.
            // These overlap the leg outputs by design - a transfer departure
            // appears in both Transfer and the departure flows.
            pManager.AddGenericParameter("Origin", "ORG", "Origin passengers starting their journey here.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Transfer", "TRF", "Transfer passengers connecting between flights.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Destination", "DST", "Destination passengers ending their journey here.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> passengers = new List<Passenger>();

            if (!DA.GetDataList(0, passengers)) return;

            // All splits come from the headless segregation logic, so the
            // canvas behaviour and the test harness share one implementation.
            List<Passenger> domestic = PassengerSegregation.Domestic(passengers);
            List<Passenger> international = PassengerSegregation.International(passengers);

            List<Passenger> male = passengers.Where(p => p != null && p.Gender == PassengerGender.Male).ToList();
            List<Passenger> female = passengers.Where(p => p != null && p.Gender == PassengerGender.Female).ToList();

            List<Passenger> checkedBags = passengers.Where(p => p != null && p.HasCheckedBag).ToList();
            List<Passenger> cabinBags = passengers.Where(p => p != null && !p.HasCheckedBag).ToList();

            List<Passenger> domesticDepartures = PassengerSegregation.DomesticDepartures(passengers);
            List<Passenger> internationalDepartures = PassengerSegregation.InternationalDepartures(passengers);
            List<Passenger> domesticArrivals = PassengerSegregation.DomesticArrivals(passengers);
            List<Passenger> internationalArrivals = PassengerSegregation.InternationalArrivals(passengers);
            List<Passenger> departures = PassengerSegregation.Departures(passengers);
            List<Passenger> arrivals = PassengerSegregation.Arrivals(passengers);

            List<Passenger> origin = PassengerSegregation.Origin(passengers);
            List<Passenger> transfer = PassengerSegregation.Transfer(passengers);
            List<Passenger> destination = PassengerSegregation.Destination(passengers);

            // Output the split flows (order matches RegisterOutputParams).
            DA.SetDataList(0, domestic);
            DA.SetDataList(1, international);
            DA.SetDataList(2, male);
            DA.SetDataList(3, female);
            DA.SetDataList(4, checkedBags);
            DA.SetDataList(5, cabinBags);

            DA.SetDataList(6, domesticDepartures);
            DA.SetDataList(7, internationalDepartures);
            DA.SetDataList(8, domesticArrivals);
            DA.SetDataList(9, internationalArrivals);
            DA.SetDataList(10, departures);
            DA.SetDataList(11, arrivals);

            DA.SetDataList(12, origin);
            DA.SetDataList(13, transfer);
            DA.SetDataList(14, destination);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("FF998877-6655-4433-2211-00AABBCCDDEE"); } }
    }
}