using AirportPlanning.Core.Inputs;
using AirportPlanning.Core.Models;
using AirportPlanning.Core.Queueing;
using AirportPlanning.Facilities.Transfer;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AirportPlanning.Components.SimulationComponent
{
    public class BaggageRecheckComponent : GH_Component
    {
        // Keep the solver alive permanently so it remembers its optimizer cache.
        private BaggageRecheckSolver _solver = new BaggageRecheckSolver();

        public BaggageRecheckComponent()
          : base(
                "Baggage Recheck",
                "T-RECHK",
                "Simulates the arrival-side baggage recheck queue. Following the TypeScript " +
                "model this is the desk facility for the International→Domestic transfer flow: " +
                "transfer passengers re-lodge checked bags and re-clear into the BHS. Serves " +
                "transfer flows whose scenario rules require baggage recheck; everyone else " +
                "passes through. The full passenger population is returned.",
                "Airport Planning", "Simulation")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter(
                "Passengers", "PAX",
                "The full passenger population. Only transfer arrivals whose scenario rules " +
                "require baggage recheck are served; everyone else passes through untouched.",
                GH_ParamAccess.list);

            pManager.AddGenericParameter(
                "Service Profile", "SP",
                "Fallback service-time profile, used only when no Service Time Map is wired.",
                GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddNumberParameter(
                "Recheck Positions", "P",
                "Physical recheck positions. One value per flow pool (a single value " +
                "broadcasts). Used when Optimize Positions is off.",
                GH_ParamAccess.list, 2);

            pManager.AddBooleanParameter(
                "Optimize Positions", "O",
                "Automatically determine the required positions.",
                GH_ParamAccess.item, false);

            pManager.AddNumberParameter(
                "Target Wait", "TW",
                "Target whole-run 95th percentile wait per flow pool in minutes (warm-up " +
                "excluded). One value per pool (a single value broadcasts). Optimization sizes " +
                "capacity until the whole run meets it, or warns at the resource limit.",
                GH_ParamAccess.list, 10.0);

            pManager.AddNumberParameter(
                "Staffing Percentage", "S%",
                "Percentage of physical positions staffed.",
                GH_ParamAccess.item, 100.0);

            pManager.AddNumberParameter(
                "Random Seed", "S",
                "Random number seed.",
                GH_ParamAccess.item, 1);

            pManager.AddGenericParameter(
                "Pool Plans", "PP",
                "Optional keyed pool plans (Pool Plans component). Match a plan to a flow pool " +
                "by the channel names reported on Pool Names, for example " +
                "'Baggage Recheck (International to domestic)'. A shared plan (no channel) " +
                "replaces every flow pool.",
                GH_ParamAccess.list);
            pManager[7].Optional = true;

            pManager.AddIntervalParameter(
                "Resource Domain", "RD",
                "Inclusive bounds for the optimizer's position search, broadcast to every pool. " +
                "Default 1 to 1200.",
                GH_ParamAccess.item,
                new Interval(1, ChannelResourcePlanResolver.DefaultMaxResources));
            pManager[8].Optional = true;

            pManager.AddBooleanParameter(
                "Split Pools per Flow", "FLW",
                "Size one pool per transfer flow, matching the TypeScript model's per-flow " +
                "sizing. When off, all recheck-required transfers share one pool.",
                GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Passengers", "PAX", "Passenger population after baggage recheck.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Queue Results", "QR", "Queue simulation results.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Physical Positions", "P", "Total physical recheck positions across every pool.", GH_ParamAccess.item);
            pManager.AddTextParameter("Pool Names", "PN", "Pool names in output order: one per transfer flow, or 'Shared'.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Physical per Pool", "PPP", "Physical recheck positions per pool, in output order.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Active per Pool", "PAP", "Staffed positions per pool, in output order.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Wait 95 per Pool", "WPP", "Whole-run 95th percentile wait per pool, in output order.", GH_ParamAccess.list);
            pManager.AddGenericParameter("Search Outcomes", "SO", "Optimizer search outcomes per pool, including every (capacity, Wait95) point evaluated. Feed an Optimizer Chart.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Nominal per Pool", "NPP", "Nominal (pre-optimization) positions per pool, in output order.", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Served", "SRV", "Transfer passengers served by the recheck queue.", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Bypassed", "BYP", "Transfer passengers whose scenario rules bypass baggage recheck.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Passenger> inPassengers = new List<Passenger>();
            ServiceProfile serviceProfile = null;
            List<double> positionsDbl = new List<double>();
            bool optimize = false;
            List<double> targetWaits = new List<double>();
            double staffingPercentage = 100.0;
            double seedDbl = 1.0;
            List<object> rawPoolPlans = new List<object>();
            Interval domain = Interval.Unset;
            bool splitPerFlow = true;

            if (!DA.GetDataList(0, inPassengers)) return;
            DA.GetData(1, ref serviceProfile);
            DA.GetDataList(2, positionsDbl);
            DA.GetData(3, ref optimize);
            DA.GetDataList(4, targetWaits);
            DA.GetData(5, ref staffingPercentage);
            DA.GetData(6, ref seedDbl);
            DA.GetDataList(7, rawPoolPlans);
            DA.GetData(8, ref domain);
            DA.GetData(9, ref splitPerFlow);

            List<PoolPlan> poolPlans = UnwrapPoolPlans(rawPoolPlans);

            BaggageRecheckInputs input = new BaggageRecheckInputs
            {
                Passengers = inPassengers,
                ServiceProfile = serviceProfile,
                PhysicalResources = ToIntList(positionsDbl),
                OptimizeResources = optimize,
                TargetWait95 = targetWaits,
                PoolPlans = poolPlans,
                SplitPoolsPerFlow = splitPerFlow,
                Seed = (int)Math.Round(seedDbl),
                StaffingPercentage = staffingPercentage,
                ResourceDomain = FacilityComponentInput.ToResourceDomain(domain)
            };

            BaggageRecheckResults finalResult = _solver.Solve(input);

            foreach (string warning in _solver.Warnings)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);

            QueueResults results = finalResult.QueueResults;

            DA.SetDataList(0, results.Passengers);
            DA.SetData(1, results);
            DA.SetData(2, finalResult.PhysicalPositions);
            DA.SetDataList(3, finalResult.PoolNames);
            DA.SetDataList(4, finalResult.PhysicalPerChannel);
            DA.SetDataList(5, finalResult.ActivePerChannel);
            DA.SetDataList(6, FacilityComponentInput.GetWaitForPools(
                finalResult.Wait95PerChannel, results, finalResult.PoolNames));
            DA.SetDataList(7, finalResult.SearchOutcomes ?? new List<ResourceSearchOutcome>());
            DA.SetDataList(8, finalResult.NominalPerChannel);
            DA.SetData(9, finalResult.ServedCount);
            DA.SetData(10, finalResult.BypassedCount);
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
        public override Guid ComponentGuid { get { return new Guid("8B4D2E3F-5C6E-4F0A-9B7D-2E3F4A5B6C7D"); } }
    }
}
