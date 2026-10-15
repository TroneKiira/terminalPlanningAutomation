using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Linq;
using System.Collections.Generic;
using AirportPlanning.Core.Models;

namespace AirportPlanning.Components.SimulationComponent
{
    public class DwellTimeComponent : GH_Component
    {
        public DwellTimeComponent()
          : base("Dwell Time (Retail Buffer)", "DWELL",
            "Simulates passengers spending time in retail/F&B using a skewed Triangular Distribution for gate arrivals.",
            "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population entering the departure lounge.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Departure Minute", "DM", "The scheduled flight departure time (in simulation minutes).", GH_ParamAccess.item);

            pManager.AddNumberParameter("Gate Open Time", "GO", "Minutes BEFORE departure when the gate opens (e.g., 50).", GH_ParamAccess.item, 50.0);
            pManager.AddNumberParameter("Gate Close Time", "GC", "Minutes BEFORE departure when the gate closes (e.g., 15).", GH_ParamAccess.item, 15.0);

            // NEW: The "Mode" of the curve. Where is the peak rush?
            pManager.AddNumberParameter("Peak Arrival %", "PK%", "Percentage into the window when the biggest rush arrives (0.0 to 1.0). Default 0.3 means 30% into the window.", GH_ParamAccess.item, 0.30);

            pManager.AddNumberParameter("Random Seed", "S", "Random number seed.", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passengers arriving at the boarding gate.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Average Dwell Time", "DT", "Average time passengers spent in the retail zone (minutes).", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            double departureMinute = 0.0;
            double gateOpen = 50.0;
            double gateClose = 15.0;
            double peakPercent = 0.30;
            double seedDbl = 1.0;

            if (!DA.GetDataList(0, inPassengers)) return;
            if (!DA.GetData(1, ref departureMinute)) return;
            DA.GetData(2, ref gateOpen);
            DA.GetData(3, ref gateClose);
            DA.GetData(4, ref peakPercent);
            DA.GetData(5, ref seedDbl);

            // Deep clone to prevent Grasshopper mutation bugs
            List<Passenger> passengers = inPassengers.Select(p => p.Clone()).ToList();
            Random rnd = new Random((int)Math.Round(seedDbl));

            double totalDwell = 0;
            int dwellCount = 0;

            foreach (Passenger p in passengers)
            {
                // Use the passenger's own scheduled departure when available
                // (windowed generation); otherwise fall back to the global DM.
                double departure = p.ScheduledDepartureTime > 0.0
                    ? p.ScheduledDepartureTime
                    : departureMinute;

                // Calculate the absolute simulation minutes for this departure
                double a = departure - gateOpen;  // Min
                double b = departure - gateClose; // Max
                double c = a + ((b - a) * peakPercent); // Mode

                if (b - a <= 0.0)
                {
                    p.QueueArrivalTime = p.ArrivalTime;
                    dwellCount++;
                    continue;
                }

                // 🧮 TRIANGULAR DISTRIBUTION MATH
                // This generates a mathematically defensible skewed bell curve
                double u = rnd.NextDouble();
                double targetGateTime = 0.0;

                double threshold = (c - a) / (b - a);

                if (u < threshold)
                {
                    // Left side of the peak (The Anxious Travelers)
                    targetGateTime = a + Math.Sqrt(u * (b - a) * (c - a));
                }
                else
                {
                    // Right side of the peak (The Dwellers and Runners)
                    targetGateTime = b - Math.Sqrt((1.0 - u) * (b - a) * (b - c));
                }

                double dwellTime = 0.0;

                if (p.ArrivalTime < targetGateTime)
                {
                    // Passenger has time to kill in retail
                    dwellTime = targetGateTime - p.ArrivalTime;
                    p.ArrivalTime = targetGateTime;
                }
                else
                {
                    // Passenger is LATE! No dwell time, just a 2-minute hustle to the gate.
                    dwellTime = 0.0;
                    p.ArrivalTime += 2.0;
                }

                p.QueueArrivalTime = p.ArrivalTime;

                // Log this in their Passport History
                p.History[FacilityType.DepartureLounge] = new FacilityRecord
                {
                    ArrivalTime = p.ArrivalTime - dwellTime,
                    StartServiceTime = p.ArrivalTime - dwellTime,
                    EndServiceTime = p.ArrivalTime,
                    WaitTime = dwellTime,
                    AssignedResource = 0
                };

                totalDwell += dwellTime;
                dwellCount++;
            }

            double avgDwell = dwellCount > 0 ? totalDwell / dwellCount : 0.0;

            DA.SetDataList(0, passengers);
            DA.SetData(1, avgDwell);
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("DDEE1122-3344-5566-7788-99AABBCCDDEE"); } }
    }
}