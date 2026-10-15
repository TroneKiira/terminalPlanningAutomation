using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Walking;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities.CheckIn;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    public class CheckInComponent : GH_Component
    {
        // Keep the solver alive permanently so it remembers its optimizer cache.
        private CheckInSolver _solver = new CheckInSolver();

        public CheckInComponent()
          : base("Check-In", "CHK",
            "Simulates passenger processing at the check-in facility.",
            "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "P", "Passenger population entering the check-in facility.", GH_ParamAccess.list);

            pManager.AddGenericParameter("Service Profile", "SP", "Fallback service-time profile, used only when no Service Time Map is wired.", GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddNumberParameter("Physical Counters", "C", "Physical check-in counters. One value per channel (a single value broadcasts). Used when Optimize Counters is off.", GH_ParamAccess.list, 8);

            pManager.AddBooleanParameter("Optimize Counters", "O", "Automatically determine the required number of counters.", GH_ParamAccess.item, false);

            pManager.AddNumberParameter("Target Wait", "TW", "Target whole-run 95th percentile waiting time per channel in minutes (warm-up excluded). One value per channel (a single value broadcasts). Optimization sizes capacity until the whole run meets it, or warns at the resource limit.", GH_ParamAccess.list, 10.0);

            pManager.AddNumberParameter("Staffing Percentage", "S%", "Percentage of physical desks actively staffed.", GH_ParamAccess.item, 100.0);

            pManager.AddNumberParameter("Random Seed", "S", "Random number seed.", GH_ParamAccess.item, 1);

            pManager.AddTextParameter("Channels", "CH", "Optional channels. Empty keeps one shared pool; otherwise each channel gets its own dedicated pool sized independently.", GH_ParamAccess.list);
            pManager[7].Optional = true;

            pManager.AddGenericParameter("Service Profiles", "SP-MAP", "Optional per-passenger service-time map (Service Time Map). Supersedes Service Profile when wired. Keys may be a profile name, a channel name, or 'profile|channel'.", GH_ParamAccess.item);
            pManager[8].Optional = true;

            pManager.AddIntervalParameter("Resource Domain", "RD", "Inclusive bounds for the optimizer's physical-counter search, broadcast to every channel. Default 1 to 1200.", GH_ParamAccess.item, new Interval(1, ChannelResourcePlanResolver.DefaultMaxResources));
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "P", "Processed passenger population after check-in.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Check-In Passengers", "CP", "Passengers that required check-in.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Queue Results", "QR", "Complete check-in simulation results.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Counters Used", "C", "Total physical counters across every pool.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Peak Queue Length", "PQ", "Maximum queue length observed during the simulation.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Average Wait", "AW", "Average passenger waiting time (minutes).", GH_ParamAccess.item);
            pManager.AddNumberParameter("Maximum Wait", "MW", "Maximum passenger waiting time (minutes).", GH_ParamAccess.item);
            pManager.AddNumberParameter("95th Percentile Wait", "W95", "Whole-run 95th percentile passenger waiting time in minutes (warm-up excluded). This is the value the Target Wait applies to.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Counter Utilization", "U", "Utilization of each check-in counter.", GH_ParamAccess.list);
            pManager.AddTextParameter("Pool Names", "PN", "Pool names in output order: each dedicated channel, or 'Shared' for a single pool.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Physical per Pool", "CPP", "Physical counters per pool, in output order.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Active per Pool", "CAP", "Staffed counters per pool, in output order.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Wait 95 per Pool", "WPP", "Whole-run 95th percentile wait per pool, in output order.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Search Outcomes", "SO", "Optimizer search outcomes per pool, including every (capacity, Wait95) point evaluated. Feed an Optimizer Chart.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            ServiceProfile serviceProfile = null;
            List<double> countersDbl = new List<double>();
            bool optimize = false;
            List<double> targetWaits = new List<double>();
            double staffingPercentage = 100.0;
            double seedDbl = 1.0;
            List<string> channels = new List<string>();
            object rawServiceProfiles = null;
            Interval domain = Interval.Unset;

            if (!DA.GetDataList(0, inPassengers)) return;
            DA.GetData(1, ref serviceProfile);
            DA.GetDataList(2, countersDbl);
            DA.GetData(3, ref optimize);
            DA.GetDataList(4, targetWaits);
            DA.GetData(5, ref staffingPercentage);
            DA.GetData(6, ref seedDbl);
            DA.GetDataList(7, channels);
            DA.GetData(8, ref rawServiceProfiles);
            DA.GetData(9, ref domain);

            IServiceTimeProvider serviceTimeProvider =
                ComponentDataHelpers.Unwrap<IServiceTimeProvider>(rawServiceProfiles);

            CheckInInputs input = new CheckInInputs
            {
                Passengers = inPassengers,
                ServiceProfile = serviceProfile,
                ServiceTimeProvider = serviceTimeProvider,
                Channels = channels,
                PhysicalResources = ToIntList(countersDbl, "Physical Counters"),
                OptimizeResources = optimize,
                TargetWait95 = targetWaits,
                Seed = (int)Math.Round(seedDbl),
                StaffingPercentage = staffingPercentage,
                ResourceDomain = FacilityComponentInput.ToResourceDomain(domain)
            };

            CheckInResults finalResult = _solver.Solve(input);

            foreach (string warning in _solver.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            QueueResults results = finalResult.QueueResults;

            List<string> poolNames = FacilityComponentInput.GetPoolNames(channels);
            List<double> waitPerPool = FacilityComponentInput.GetWaitPerPool(
                finalResult.Wait95PerChannel, results, channels);

            DA.SetDataList(0, results.Passengers);
            DA.SetDataList(1, results.ProcessedPassengers);
            DA.SetData(2, results);
            DA.SetData(3, finalResult.PhysicalCounters);
            DA.SetData(4, results.PeakQueueLength);
            DA.SetData(5, results.AverageWait);
            DA.SetData(6, results.MaxWait);
            DA.SetData(7, results.Wait95);
            DA.SetDataList(8, results.CounterUtilization);
            DA.SetDataList(9, poolNames);
            DA.SetDataList(10, finalResult.PhysicalPerChannel);
            DA.SetDataList(11, finalResult.ActivePerChannel);
            DA.SetDataList(12, waitPerPool);
            DA.SetDataList(13, finalResult.SearchOutcomes ?? new List<ResourceSearchOutcome>());
        }

        private static List<int> ToIntList(List<double> values, string label)
        {
            List<int> result = new List<int>();

            if (values == null)
                return result;

            foreach (double value in values)
            {
                int rounded = (int)Math.Round(value);

                result.Add(rounded < 1 ? 1 : rounded);
            }

            return result;
        }

        protected override System.Drawing.Bitmap Icon { get { return null; } }
        public override Guid ComponentGuid { get { return new Guid("8427233C-E67D-4A10-A647-BA8D420BA2F9"); } }
    }
}
