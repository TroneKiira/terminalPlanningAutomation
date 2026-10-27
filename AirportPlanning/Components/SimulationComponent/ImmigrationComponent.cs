using AirportPlanning.Core.Distributions;
using AirportPlanning.Core.Generation;
using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Core.Walking;
using AirportPlanning.Facilities.CheckIn;
using AirportPlanning.Facilities.Immigration;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    public class ImmigrationComponent : GH_Component
    {
        // Keep the solver alive permanently so it remembers its optimizer cache.
        private ImmigrationSolver _solver = new ImmigrationSolver();

        public ImmigrationComponent()
          : base("Immigration", "IMM", "Simulates passenger processing at immigration.", "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population entering immigration.", GH_ParamAccess.list);

            pManager.AddGenericParameter("Service Profile", "SP", "Fallback service-time profile, used only when no Service Time Map is wired.", GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddNumberParameter("Immigration Booths", "B", "Physical immigration booths. One value per channel (a single value broadcasts). Used when Optimize Booths is off.", GH_ParamAccess.list, 8);

            pManager.AddBooleanParameter("Optimize Booths", "O", "Automatically determine required booths.", GH_ParamAccess.item, false);

            pManager.AddNumberParameter("Target Wait", "TW", "Target whole-run 95th percentile wait per channel in minutes (warm-up excluded). One value per channel (a single value broadcasts). Optimization sizes capacity until the whole run meets it, or warns at the resource limit.", GH_ParamAccess.list, 10.0);

            pManager.AddNumberParameter("Staffing Percentage", "S%", "Percentage of physical booths staffed.", GH_ParamAccess.item, 100.0);

            pManager.AddNumberParameter("Random Seed", "S", "Random number seed.", GH_ParamAccess.item, 1);

            pManager.AddTextParameter("Channels", "CH", "Optional channels. Empty keeps one shared pool; otherwise each channel gets its own dedicated pool sized independently.", GH_ParamAccess.list);
            pManager[7].Optional = true;

            pManager.AddGenericParameter("Service Profiles", "SP-MAP", "Optional per-passenger service-time map (Service Time Map). Supersedes Service Profile when wired. Keys may use 'Channel|DHPType|ProfileName' (same as a Pool Plan identity), a profile name, a channel name, or 'profile|channel'.", GH_ParamAccess.item);
            pManager[8].Optional = true;

            pManager.AddIntervalParameter("Resource Domain", "RD", "Inclusive bounds for the optimizer's physical-booth search, broadcast to every channel. Default 1 to 1200.", GH_ParamAccess.item, new Interval(1, ChannelResourcePlanResolver.DefaultMaxResources));
            pManager[9].Optional = true;

            pManager.AddGenericParameter("Pool Plans", "PP", "Optional keyed pool plans (Pool Plans component). When wired, they define each pool's nominal resources, target wait and resource domain.", GH_ParamAccess.list);
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population after immigration.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Queue Results", "QR", "Queue simulation results.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Physical Booths", "B", "Total physical booths across every pool.", GH_ParamAccess.item);
            pManager.AddTextParameter("Pool Names", "PN", "Pool names in output order: each dedicated channel, or 'Shared' for a single pool.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Physical per Pool", "BPP", "Physical booths per pool, in output order.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Active per Pool", "BAP", "Staffed booths per pool, in output order.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Wait 95 per Pool", "WPP", "Whole-run 95th percentile wait per pool, in output order.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Search Outcomes", "SO", "Optimizer search outcomes per pool, including every (capacity, Wait95) point evaluated. Feed an Optimizer Chart.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Nominal per Pool", "NPP", "Nominal (PNR) physical booths per pool, before optimization.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            ServiceProfile serviceProfile = null;
            List<double> boothsDbl = new List<double>();
            bool optimize = false;
            List<double> targetWaits = new List<double>();
            double staffingPercentage = 100.0;
            double seedDbl = 1.0;
            List<string> channels = new List<string>();
            object rawServiceProfiles = null;
            List<object> rawPoolPlans = new List<object>();
            Interval domain = Interval.Unset;

            if (!DA.GetDataList(0, inPassengers)) return;
            DA.GetData(1, ref serviceProfile);
            DA.GetDataList(2, boothsDbl);
            DA.GetData(3, ref optimize);
            DA.GetDataList(4, targetWaits);
            DA.GetData(5, ref staffingPercentage);
            DA.GetData(6, ref seedDbl);
            DA.GetDataList(7, channels);
            DA.GetData(8, ref rawServiceProfiles);
            DA.GetData(9, ref domain);
            DA.GetDataList(10, rawPoolPlans);

            IServiceTimeProvider serviceTimeProvider =
                ComponentDataHelpers.Unwrap<IServiceTimeProvider>(rawServiceProfiles);
            List<PoolPlan> poolPlans = UnwrapPoolPlans(rawPoolPlans);

            ImmigrationInputs input = new ImmigrationInputs
            {
                Passengers = inPassengers,
                ServiceProfile = serviceProfile,
                ServiceTimeProvider = serviceTimeProvider,
                Channels = channels,
                PhysicalResources = ToIntList(boothsDbl),
                OptimizeResources = optimize,
                TargetWait95 = targetWaits,
                PoolPlans = poolPlans,
                Seed = (int)Math.Round(seedDbl),
                StaffingPercentage = staffingPercentage,
                ResourceDomain = FacilityComponentInput.ToResourceDomain(domain)
            };

            ImmigrationResults finalResult = _solver.Solve(input);

            foreach (string warning in _solver.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            QueueResults results = finalResult.QueueResults;

            DA.SetDataList(0, results.Passengers);
            DA.SetData(1, results);
            DA.SetData(2, finalResult.PhysicalCounters);
            DA.SetDataList(3, finalResult.PoolNames);
            DA.SetDataList(4, finalResult.PhysicalPerChannel);
            DA.SetDataList(5, finalResult.ActivePerChannel);
            DA.SetDataList(6, FacilityComponentInput.GetWaitForPools(
                finalResult.Wait95PerChannel, results, finalResult.PoolNames));
            DA.SetDataList(7, finalResult.SearchOutcomes ?? new List<ResourceSearchOutcome>());
            DA.SetDataList(8, finalResult.NominalPerChannel);
        }

        private static List<PoolPlan> UnwrapPoolPlans(List<object> raw)
        {
            List<PoolPlan> plans = new List<PoolPlan>();

            if (raw == null)
                return plans;

            foreach (object item in raw)
            {
                PoolPlan plan = ComponentDataHelpers.Unwrap<PoolPlan>(item);
                if (plan != null)
                    plans.Add(plan);
            }

            return plans;
        }

        private static List<int> ToIntList(List<double> values)
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
        public override Guid ComponentGuid { get { return new Guid("4E63995C-ECAF-47A9-A077-A940030621B6"); } }
    }
}
